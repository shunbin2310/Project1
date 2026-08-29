using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.UnitsOfMeasure;

public sealed class CreateUnitOfMeasureRequest
{
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}
