using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.ProductCategories;

public sealed class CreateProductCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}
