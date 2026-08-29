using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Products;

public sealed class UpdateProductRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    [Range(1, int.MaxValue)]
    public int ProductCategoryId { get; init; }

    [Range(1, int.MaxValue)]
    public int UnitOfMeasureId { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal DefaultUnitPrice { get; init; }

    [Range(typeof(decimal), "0", "999999999999999.999")]
    public decimal ReorderLevel { get; init; }

    public bool IsActive { get; init; }
}
