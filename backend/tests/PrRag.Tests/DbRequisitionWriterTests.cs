using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

public class DbRequisitionWriterTests : IAsyncLifetime
{
    private static string ConnectionTemplate => TestDatabase.ConnectionStringTemplate;

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        _connectionString = $"{ConnectionTemplate};Database={_dbName}";
        (_provider, _, _) = IntegrationServiceFactory.Create(_connectionString);
        await TestDatabase.MigrateAndReloadTypesAsync(_provider);
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            await TestDatabase.DropDatabaseAsync(_dbName);
        }
    }

    private static NewPurchaseRequisition ValidRequisition() => new()
    {
        SupplierCode = "SUP000001",
        Item = "ITM0001",
        Description = "Hydraulic pump for maintenance.",
        Quantity = 2.5m,
        Date = "2026-09-30",
        Requester = "Ana Souza",
    };

    [Fact]
    public async Task Valid_requisition_persists_row_with_exactly_the_six_fields()
    {
        using var scope = _provider!.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IRequisitionWriter>();

        var result = await writer.WriteAsync(ValidRequisition());

        Assert.True(result.Success);
        Assert.NotNull(result.RequisitionId);
        Assert.True(Guid.TryParseExact(result.RequisitionId, "N", out _));

        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        var stored = await db.CreatedRequisitions.SingleAsync(r => r.Id == Guid.ParseExact(result.RequisitionId, "N"));

        Assert.Equal("SUP000001", stored.SupplierCode);
        Assert.Equal("ITM0001", stored.Item);
        Assert.Equal("Hydraulic pump for maintenance.", stored.Description);
        Assert.Equal(2.5m, stored.Quantity);
        Assert.Equal("2026-09-30", stored.Date);
        Assert.Equal("Ana Souza", stored.Requester);
    }

    [Theory]
    [InlineData(true, false, false, false, false, false)] // missing supplier code
    [InlineData(false, true, false, false, false, false)] // missing item
    [InlineData(false, false, true, false, false, false)] // missing description
    [InlineData(false, false, false, true, false, false)] // non-positive quantity
    [InlineData(false, false, false, false, true, false)] // invalid date
    [InlineData(false, false, false, false, false, true)] // missing requester
    public async Task Invalid_or_missing_fields_persist_nothing(
        bool badSupplier,
        bool badItem,
        bool badDescription,
        bool badQuantity,
        bool badDate,
        bool badRequester)
    {
        var requisition = ValidRequisition();
        if (badSupplier)
        {
            requisition.SupplierCode = string.Empty;
        }
        if (badItem)
        {
            requisition.Item = string.Empty;
        }
        if (badDescription)
        {
            requisition.Description = string.Empty;
        }
        if (badQuantity)
        {
            requisition.Quantity = 0;
        }
        if (badDate)
        {
            requisition.Date = "not-a-date";
        }
        if (badRequester)
        {
            requisition.Requester = string.Empty;
        }

        using var scope = _provider!.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IRequisitionWriter>();

        var result = await writer.WriteAsync(requisition);

        Assert.False(result.Success);
        Assert.Null(result.RequisitionId);
        Assert.Contains("required fields", result.Error);

        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Equal(0, await db.CreatedRequisitions.CountAsync());
    }

    [Fact]
    public async Task Distinct_writes_produce_distinct_ids()
    {
        using var scope = _provider!.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IRequisitionWriter>();

        var first = await writer.WriteAsync(ValidRequisition());
        var second = await writer.WriteAsync(ValidRequisition());

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.RequisitionId, second.RequisitionId);

        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Equal(2, await db.CreatedRequisitions.CountAsync());
    }
}