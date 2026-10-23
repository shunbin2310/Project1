using System.ComponentModel.DataAnnotations;
using Project1.Api.Validation;

namespace Project1.Api.DTOs.Products;

public sealed class CreateProductRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    [StringLength(100)]
    public string? StorageLocation { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal DefaultUnitPrice { get; init; }

    [Range(typeof(decimal), "0", "999999999999999")]
    [WholeNumber]
    public decimal ReorderLevel { get; init; }
}
