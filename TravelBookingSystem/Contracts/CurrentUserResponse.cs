namespace TravelBookingSystem.Contracts;

public sealed record CurrentUserResponse(
    string Id, string FirstName, string LastName, string? Email, string? PhoneNumber, IReadOnlyList<string> Roles);
