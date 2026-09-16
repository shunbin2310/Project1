using Project1.Api.Entities;

namespace Project1.Api.DTOs.GoodsReceipts;

public sealed record GoodsReceiptResponse(
    int Id,
    string GoodsReceiptNumber,
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierDeliveryNoteNumber,
    DateOnly ReceivedDate,
    string? Notes,
    GoodsReceiptStatus Status,
    int CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PostedAtUtc,
    int? PostedByUserId,
    string? PostedByName,
    IReadOnlyList<GoodsReceiptItemResponse> Items);
