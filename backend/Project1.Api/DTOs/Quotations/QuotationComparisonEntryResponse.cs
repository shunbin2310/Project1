using Project1.Api.Entities;

namespace Project1.Api.DTOs.Quotations;

public sealed record QuotationComparisonEntryResponse(
    int QuotationId,
    string QuotationNumber,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierQuotationReference,
    DateOnly QuotationDate,
    DateOnly? ValidUntil,
    QuotationStatus Status,
    decimal TotalAmount,
    bool IsLowestTotal,
    bool IsSelected,
    IReadOnlyList<QuotationItemResponse> Items);
