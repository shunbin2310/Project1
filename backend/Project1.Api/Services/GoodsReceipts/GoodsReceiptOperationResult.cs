using Project1.Api.DTOs.GoodsReceipts;

namespace Project1.Api.Services.GoodsReceipts;

public enum GoodsReceiptOperationStatus
{
    Success,
    NotFound,
    ValidationFailed,
    InvalidState,
    DuplicateDraft,
    DuplicateDeliveryNote,
    QuantityExceeded
}

public sealed record GoodsReceiptOperationResult(
    GoodsReceiptOperationStatus Status,
    GoodsReceiptResponse? GoodsReceipt = null,
    string? ErrorMessage = null);
