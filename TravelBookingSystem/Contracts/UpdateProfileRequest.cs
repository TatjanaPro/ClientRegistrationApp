using System.ComponentModel.DataAnnotations;

namespace TravelBookingSystem.Contracts;

public sealed class UpdateProfileRequest
{
    [Required, StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; init; }
}
