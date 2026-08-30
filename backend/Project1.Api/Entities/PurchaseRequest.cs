namespace Project1.Api.Entities;

using Project1.Api.Entities.Identity;

public sealed class PurchaseRequest
{
    public int Id { get; set; }

    public string RequestNumber { get; set; } = string.Empty;

    public string? RequesterName { get; set; }

    public int? RequesterUserId { get; set; }

    public ApplicationUser? RequesterUser { get; set; }

    public int? DepartmentId { get; set; }

    public Department? Department { get; set; }

    public DateOnly? RequiredDate { get; set; }

    public string? Justification { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public ICollection<PurchaseRequestItem> Items { get; set; } = [];
}
