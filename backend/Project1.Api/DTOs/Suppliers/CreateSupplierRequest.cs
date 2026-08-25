using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Suppliers;

public sealed class CreateSupplierRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(100)]
    public string? ContactPerson { get; init; }

    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; init; }

    [StringLength(30)]
    public string? Phone { get; init; }

    [StringLength(500)]
    public string? Address { get; init; }
}
