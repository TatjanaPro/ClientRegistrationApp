using Microsoft.AspNetCore.Identity;

namespace TravelBookingSystem.Models;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Booking> Bookings { get; set; }
        = new List<Booking>();
}