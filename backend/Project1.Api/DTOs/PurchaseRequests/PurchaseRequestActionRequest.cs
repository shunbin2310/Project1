using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed class PurchaseRequestActionRequest
{
    [StringLength(500)]
    public string? Comment { get; init; }
}
