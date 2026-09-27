using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrRag.Api;
using PrRag.Application;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Application.Configuration;
using PrRag.Application.Domain;
using PrRag.Infrastructure;
using PrRag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// A bootstrap logger, because AddInfrastructure resolves and validates the LLM
// provider while the container is still being built. Disposed with the entry point.
using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging
    .AddConfiguration(builder.Configuration)
    .AddConsole());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration,
    bootstrapLoggerFactory.CreateLogger("PrRag.Startup"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    var configured = builder.Configuration["Cors__AllowedOrigins"]
        ?? "http://localhost:5173";

    var origins = configured
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct()
        .ToArray();

    options.AddDefaultPolicy(policy => policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PrRagDbContext>();

var app = builder.Build();

await DbInitializer.ApplyMigrationsAsync(app.Services);

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

app.MapPost("/api/chat", async (
    ChatRequest request,
    IChatService chatService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question is required." });
    }

    try
    {
        var response = await chatService.AnswerAsync(request, ct);
        return Results.Ok(response);
    }
    catch (ChatTurnFailedException ex)
    {
        // 502, not 500: the turn failed because the model provider did, and the
        // distinction matters to a caller deciding whether to retry. Answering
        // 200 with a blank body instead made an outage indistinguishable from a
        // quiet turn.
        app.Logger.LogError(ex, "Chat turn failed: the agent run produced no answer");
        return Results.Problem(
            title: "The assistant produced no answer.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/api/chat/stream", async (
    ChatStreamRequest request,
    IChatService chatService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question is required." });
    }

    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";

    try
    {
        await foreach (var token in chatService.StreamAsync(request, ct))
        {
            foreach (var line in token.Split('\n'))
            {
                await httpContext.Response.WriteAsync($"data: {line}\n", ct);
            }
            await httpContext.Response.WriteAsync("\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }

        // Reached only when the run produced text. StreamAsync throws otherwise,
        // so [DONE] can never follow an empty body: a client that saw the
        // terminator after no content would render a completed, empty answer
        // rather than a failure. The status line is already sent by this point,
        // so the failure reaches the client as a truncated stream, which is the
        // honest signal available here.
        await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Client aborted the stream; nothing more to write.
    }
    catch (ChatTurnFailedException ex)
    {
        app.Logger.LogError(ex, "Chat stream failed: the agent run produced no answer");
        httpContext.Abort();
    }

    return Results.Empty;
});

app.MapPost("/api/ingest", async (
    IIngestionService ingestionService,
    CancellationToken ct) =>
{
    var result = await ingestionService.IngestAsync(ct);
    return Results.Ok(result);
});

app.MapGet("/api/status", async (
    IStatusService statusService,
    CancellationToken ct) =>
{
    var status = await statusService.GetStatusAsync(ct);
    return Results.Ok(status);
});

app.MapDelete("/api/sessions/{id}", SessionEndpoints.Discard);

app.MapGet("/api/created-requisitions", CreatedRequisitionEndpoints.List);

app.Run();
