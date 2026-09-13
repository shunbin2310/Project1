namespace Project1.Api.DTOs.Quotations;

public sealed record QuotationComparisonResponse(
    int PurchaseRequestId,
    string PurchaseRequestNumber,
    int? SelectedQuotationId,
    decimal? LowestTotalAmount,
    IReadOnlyList<QuotationComparisonEntryResponse> Quotations);
