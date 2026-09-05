namespace PrRag.Application.DTOs;

public sealed class IngestResult
{
    public int TotalRecords { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Embedded { get; set; }
}

public sealed class SystemStatus
{
    public int RequisitionCount { get; set; }
    public int EmbeddedCount { get; set; }
    public DateTime? LastSync { get; set; }
}
