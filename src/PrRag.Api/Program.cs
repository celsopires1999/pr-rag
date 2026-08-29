using Microsoft.EntityFrameworkCore;
using PrRag.Application;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Application.Configuration;
using PrRag.Infrastructure;
using PrRag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PrRagDbContext>();

var app = builder.Build();

await DbInitializer.ApplyMigrationsAsync(app.Services);

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

    var response = await chatService.AnswerAsync(request, ct);
    return Results.Ok(response);
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

app.Run();
