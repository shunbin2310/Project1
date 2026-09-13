using Project1.Api.Entities;

namespace Project1.Api.DTOs.Quotations;

public sealed record QuotationResponse(
    int Id,
    string QuotationNumber,
    int PurchaseRequestId,
    string PurchaseRequestNumber,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierQuotationReference,
    DateOnly QuotationDate,
    DateOnly? ValidUntil,
    string? Notes,
    QuotationStatus Status,
    decimal TotalAmount,
    int CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? SelectedAtUtc,
    IReadOnlyList<QuotationItemResponse> Items);
