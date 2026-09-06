using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Users;

public sealed class CreateUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    public int? DepartmentId { get; init; }

    [Required]
    [MinLength(1)]
    public IReadOnlyList<string> Roles { get; init; } = [];
}
