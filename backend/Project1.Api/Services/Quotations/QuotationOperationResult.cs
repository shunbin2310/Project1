using Project1.Api.DTOs.Quotations;

namespace Project1.Api.Services.Quotations;

public enum QuotationOperationStatus
{
    Success,
    NotFound,
    ValidationFailed,
    InvalidState,
    DuplicateQuotation,
    SupplierUnavailable,
    SupplierCannotSupplyProducts
}

public sealed record QuotationOperationResult(
    QuotationOperationStatus Status,
    QuotationResponse? Quotation = null,
    string? ErrorMessage = null);
