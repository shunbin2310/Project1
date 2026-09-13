using Project1.Api.Entities;

namespace Project1.Api.DTOs.PurchaseOrders;

public sealed record PurchaseOrderResponse(
    int Id,
    string PurchaseOrderNumber,
    int QuotationId,
    string QuotationNumber,
    int PurchaseRequestId,
    string PurchaseRequestNumber,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierQuotationReference,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string? DeliveryAddress,
    string? Notes,
    PurchaseOrderStatus Status,
    decimal TotalAmount,
    int CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? IssuedAtUtc,
    int? IssuedByUserId,
    string? IssuedByName,
    DateTimeOffset? CancelledAtUtc,
    int? CancelledByUserId,
    string? CancelledByName,
    string? CancellationReason,
    IReadOnlyList<PurchaseOrderItemResponse> Items);
