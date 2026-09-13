using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseOrders;

public sealed class CancelPurchaseOrderRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;
}
