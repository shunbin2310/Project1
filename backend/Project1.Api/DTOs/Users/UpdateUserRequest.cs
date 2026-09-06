using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Users;

public sealed class UpdateUserRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    public int? DepartmentId { get; init; }

    [Required]
    [MinLength(1)]
    public IReadOnlyList<string> Roles { get; init; } = [];
}
