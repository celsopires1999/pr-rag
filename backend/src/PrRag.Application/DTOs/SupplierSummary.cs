namespace PrRag.Application.DTOs;

/// <summary>Distinct supplier (code + name) that supplied a given item.</summary>
public sealed record SupplierSummary(string SupplierCode, string SupplierName);