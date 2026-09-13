using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseOrders;

public sealed class CreatePurchaseOrderRequest
{
    [Range(1, int.MaxValue)]
    public int QuotationId { get; init; }

    public DateOnly OrderDate { get; init; }

    public DateOnly? ExpectedDeliveryDate { get; init; }

    [StringLength(500)]
    public string? DeliveryAddress { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }
}
