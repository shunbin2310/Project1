using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed class UpdatePurchaseRequestRequest
{
    public DateOnly? RequiredDate { get; init; }

    [StringLength(1000)]
    public string? Justification { get; init; }

    public IReadOnlyList<PurchaseRequestItemRequest> Items { get; init; } = [];
}
