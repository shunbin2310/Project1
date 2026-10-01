using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseOrders;

public sealed class PurchaseOrderActionRequest
{
    [StringLength(500)]
    public string? Comment { get; init; }
}
