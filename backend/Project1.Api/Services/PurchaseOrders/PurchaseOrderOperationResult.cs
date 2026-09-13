using Project1.Api.DTOs.PurchaseOrders;

namespace Project1.Api.Services.PurchaseOrders;

public enum PurchaseOrderOperationStatus
{
    Success,
    NotFound,
    ValidationFailed,
    InvalidState,
    DuplicatePurchaseOrder,
    QuotationNotSelected,
    SupplierUnavailable
}

public sealed record PurchaseOrderOperationResult(
    PurchaseOrderOperationStatus Status,
    PurchaseOrderResponse? PurchaseOrder = null,
    string? ErrorMessage = null);
