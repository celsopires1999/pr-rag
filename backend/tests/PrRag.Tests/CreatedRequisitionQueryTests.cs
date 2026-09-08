using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

public class CreatedRequisitionQueryTests : IAsyncLifetime
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
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            await TestDatabase.DropDatabaseAsync(_dbName);
        }
    }

    private static CreatedRequisition Row(
        int createdMinutesAgo,
        string supplierCode,
        string item,
        string description,
        decimal quantity,
        string date,
        string requester,
        string? sessionId) => new()
    {
        SupplierCode = supplierCode,
        Item = item,
        Description = description,
        Quantity = quantity,
        Date = date,
        Requester = requester,
        SessionId = sessionId,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-createdMinutesAgo),
    };

    private async Task SeedAsync()
    {
        var rows = new[]
        {
            // created most recently first in default (CreatedAt desc) order:
            Row(5, "SUP000001", "ITM0001", "Hydraulic pump for maintenance.", 2.5m, "2026-09-30", "Ana Souza", "alpha"),
            Row(10, "SUP000002", "ITM0002", "Ball bearings for assembly.", 5m, "2026-10-01", "Carlos Lima", "beta"),
            Row(15, "SUP000003", "ITM0001", "Steel sheet for fabrication.", 1.25m, "2026-09-15", "Ana Souza", null),
            Row(20, "SUP000001", "ITM0003", "Threaded bolts assortment.", 10m, "2026-10-05", "Maria Duarte", "alpha"),
            Row(25, "SUP000002", "ITM0001", "Lubricating oil drums.", 3m, "2026-11-01", "Carlos Lima", "beta"),
        };

        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        db.CreatedRequisitions.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task<CreatedRequisitionPage> QueryAsync(CreatedRequisitionQueryInput input)
    {
        using var scope = _provider!.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ICreatedRequisitionQuery>();
        return await query.QueryAsync(input);
    }

    [Fact]
    public async Task Listing_returns_all_requisitions_ordered_by_created_at_descending()
    {
        var page = await QueryAsync(new CreatedRequisitionQueryInput());

        Assert.Equal(5, page.Total);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);

        var createdAts = page.Items.Select(i => i.CreatedAt).ToList();
        Assert.Equal(createdAts.OrderByDescending(x => x), createdAts);
        Assert.Contains(page.Items, i => i.SessionId == "alpha");
        Assert.Contains(page.Items, i => i.SessionId is null);
    }

    [Fact]
    public async Task Pagination_returns_the_correct_slice()
    {
        var page = await QueryAsync(new CreatedRequisitionQueryInput { Page = 2, PageSize = 2 });

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("SUP000003", page.Items[0].SupplierCode);
        Assert.Equal("SUP000001", page.Items[1].SupplierCode);
    }

    [Fact]
    public async Task Sorting_orders_by_the_selected_column_and_direction()
    {
        var ascending = await QueryAsync(new CreatedRequisitionQueryInput
        {
            SortField = CreatedRequisitionSortField.Quantity,
            SortDescending = false,
        });
        var quantities = ascending.Items.Select(i => i.Quantity).ToList();
        Assert.Equal(quantities.OrderBy(x => x), quantities);

        var descending = await QueryAsync(new CreatedRequisitionQueryInput
        {
            SortField = CreatedRequisitionSortField.Quantity,
            SortDescending = true,
        });
        var quantitiesDesc = descending.Items.Select(i => i.Quantity).ToList();
        Assert.Equal(quantitiesDesc.OrderByDescending(x => x), quantitiesDesc);
        Assert.Equal(10m, descending.Items[0].Quantity);
    }

    [Theory]
    [InlineData("SUP000001", new[] { "SUP000001", "SUP000001" })]
    [InlineData("ITM0001", new[] { "ITM0001", "ITM0001", "ITM0001" })]
    [InlineData("pump", new[] { "Hydraulic pump for maintenance." })]
    [InlineData("ANA", new[] { "Ana Souza", "Ana Souza" })]
    public async Task Text_filters_match_case_insensitively(string filter, string[] expectedItems)
    {
        var page = await QueryAsync(new CreatedRequisitionQueryInput
        {
            SupplierCode = filter.StartsWith("SUP", StringComparison.OrdinalIgnoreCase) ? filter : null,
            Item = filter.StartsWith("ITM", StringComparison.OrdinalIgnoreCase) ? filter : null,
            Description = filter == "pump" ? filter : null,
            Requester = filter == "ANA" ? filter : null,
        });

        Assert.Equal(expectedItems.Length, page.Total);
        Assert.All(page.Items, i => Assert.Contains(
            new[] { i.SupplierCode, i.Item, i.Description, i.Requester },
            value => string.Equals(value, expectedItems[0], StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Quantity_range_filters_rows()
    {
        var page = await QueryAsync(new CreatedRequisitionQueryInput
        {
            MinQuantity = 2.5m,
            MaxQuantity = 5m,
        });

        Assert.Equal(3, page.Total);
        Assert.All(page.Items, i => Assert.InRange(i.Quantity, 2.5m, 5m));
    }

    [Fact]
    public async Task Date_range_filters_rows()
    {
        var page = await QueryAsync(new CreatedRequisitionQueryInput
        {
            DateFrom = new DateOnly(2026, 10, 1),
            DateTo = new DateOnly(2026, 10, 31),
        });

        Assert.Equal(2, page.Total);
        Assert.Equal(
            new[] { "2026-10-01", "2026-10-05" }.OrderBy(x => x),
            page.Items.Select(i => i.Date).OrderBy(x => x));
    }

    [Fact]
    public async Task Created_at_range_filters_rows()
    {
        using (var scope = _provider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
            db.CreatedRequisitions.AddRange(
                new CreatedRequisition
                {
                    SupplierCode = "SUP000009",
                    Item = "ITM0009",
                    Description = "Historical row inside range.",
                    Quantity = 1m,
                    Date = "2026-01-02",
                    Requester = "Ana Souza",
                    CreatedAt = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero),
                },
                new CreatedRequisition
                {
                    SupplierCode = "SUP000010",
                    Item = "ITM00010",
                    Description = "Historical row before range.",
                    Quantity = 1m,
                    Date = "2026-01-05",
                    Requester = "Carlos Lima",
                    CreatedAt = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero),
                });
            await db.SaveChangesAsync();
        }

        var page = await QueryAsync(new CreatedRequisitionQueryInput
        {
            CreatedFrom = new DateOnly(2026, 1, 1),
            CreatedTo = new DateOnly(2026, 1, 3),
        });

        var row = Assert.Single(page.Items);
        Assert.Equal("SUP000009", row.SupplierCode);
    }

    [Fact]
    public async Task Session_scope_returns_only_that_sessions_rows()
    {
        var alpha = await QueryAsync(new CreatedRequisitionQueryInput { SessionId = "alpha" });
        Assert.Equal(2, alpha.Total);
        Assert.All(alpha.Items, i => Assert.Equal("alpha", i.SessionId));

        var beta = await QueryAsync(new CreatedRequisitionQueryInput { SessionId = "beta" });
        Assert.Equal(2, beta.Total);
        Assert.All(beta.Items, i => Assert.Equal("beta", i.SessionId));

        var unknown = await QueryAsync(new CreatedRequisitionQueryInput { SessionId = "nope" });
        Assert.Equal(0, unknown.Total);
    }

    [Fact]
    public async Task Unscoped_query_includes_requisitions_without_a_session()
    {
        // The null-session row ("SUP000003") only appears when no session filter is set.
        var scoped = await QueryAsync(new CreatedRequisitionQueryInput { SessionId = "beta" });
        Assert.DoesNotContain(scoped.Items, i => i.SupplierCode == "SUP000003");

        var unscoped = await QueryAsync(new CreatedRequisitionQueryInput());
        Assert.Contains(unscoped.Items, i => i.SupplierCode == "SUP000003" && i.SessionId is null);
    }

    [Fact]
    public async Task Writer_accepts_absent_session_id_and_persists_it_as_null()
    {
        using var scope = _provider!.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IRequisitionWriter>();

        var result = await writer.WriteAsync(new NewPurchaseRequisition
        {
            SupplierCode = "SUP000001",
            Item = "ITM0001",
            Description = "Legacy requisition without a session.",
            Quantity = 1m,
            Date = "2026-12-01",
            Requester = "Ana Souza",
        });

        Assert.True(result.Success);

        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        var stored = await db.CreatedRequisitions.SingleAsync(r => r.Id == Guid.ParseExact(result.RequisitionId!, "N"));
        Assert.Null(stored.SessionId);
    }
}