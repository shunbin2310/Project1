using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Users;

public sealed class SetUserActiveRequest
{
    [Required]
    public bool? IsActive { get; init; }
}
