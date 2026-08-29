using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.UnitsOfMeasure;

public sealed class UpdateUnitOfMeasureRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public bool IsActive { get; init; }
}
