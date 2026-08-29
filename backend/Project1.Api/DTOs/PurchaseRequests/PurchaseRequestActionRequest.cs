using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed class PurchaseRequestActionRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string ActionBy { get; init; } = string.Empty;

    public IReadOnlyList<string> ActorRoles { get; init; } = [];

    [StringLength(500)]
    public string? Comment { get; init; }
}
