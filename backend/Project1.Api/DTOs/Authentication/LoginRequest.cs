using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Authentication;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Password { get; init; } = string.Empty;
}
