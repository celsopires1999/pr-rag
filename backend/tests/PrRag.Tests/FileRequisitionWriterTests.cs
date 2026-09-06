using System.Text.Json;
using Microsoft.Extensions.Options;
using PrRag.Application.Configuration;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Services;
using Xunit;

namespace PrRag.Tests;

public class FileRequisitionWriterTests : IDisposable
{
    private readonly string _dir;
    private readonly FileRequisitionWriter _writer;

    public FileRequisitionWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prrag-req-{Guid.NewGuid():N}");
        _writer = new FileRequisitionWriter(Options.Create(new RequisitionsSettings { Directory = _dir }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
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
    public async Task Valid_requisition_writes_json_with_exactly_the_six_fields()
    {
        var result = await _writer.WriteAsync(ValidRequisition());

        Assert.True(result.Success);
        Assert.NotNull(result.FileName);
        Assert.True(File.Exists(Path.Combine(_dir, result.FileName!)));

        var json = await File.ReadAllTextAsync(Path.Combine(_dir, result.FileName!));
        var written = JsonSerializer.Deserialize<NewPurchaseRequisition>(json)!;
        Assert.Equal("SUP000001", written.SupplierCode);
        Assert.Equal("ITM0001", written.Item);
        Assert.Equal("Hydraulic pump for maintenance.", written.Description);
        Assert.Equal(2.5m, written.Quantity);
        Assert.Equal("2026-09-30", written.Date);
        Assert.Equal("Ana Souza", written.Requester);

        using var doc = JsonDocument.Parse(json);
        var properties = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Date", "Description", "Item", "Quantity", "Requester", "SupplierCode" }, properties);
    }

    [Theory]
    [InlineData(true, false, false, false, false, false)] // missing supplier code
    [InlineData(false, true, false, false, false, false)] // missing item
    [InlineData(false, false, true, false, false, false)] // missing description
    [InlineData(false, false, false, true, false, false)] // non-positive quantity
    [InlineData(false, false, false, false, true, false)] // invalid date
    [InlineData(false, false, false, false, false, true)] // missing requester
    public async Task Invalid_or_missing_fields_write_no_file_and_return_error(
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

        var result = await _writer.WriteAsync(requisition);

        Assert.False(result.Success);
        Assert.Null(result.FileName);
        Assert.Contains("required fields", result.Error);

        if (Directory.Exists(_dir))
        {
            Assert.Empty(Directory.GetFiles(_dir, "*.json"));
        }
    }
}