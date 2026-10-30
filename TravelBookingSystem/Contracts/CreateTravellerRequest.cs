
namespace TravelBookingSystem.Contracts;

public sealed class CreateTravellerRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public DateTime DateOfBirth { get; set; }
}

