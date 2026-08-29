using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed class CreatePurchaseRequestRequest
{
    [StringLength(100)]
    public string? RequesterName { get; init; }

    [Range(1, int.MaxValue)]
    public int? DepartmentId { get; init; }

    public DateOnly? RequiredDate { get; init; }

    [StringLength(1000)]
    public string? Justification { get; init; }

    public IReadOnlyList<PurchaseRequestItemRequest> Items { get; init; } = [];
}
