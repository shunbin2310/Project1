using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Departments;

public sealed class UpdateDepartmentRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public bool IsActive { get; init; }
}
