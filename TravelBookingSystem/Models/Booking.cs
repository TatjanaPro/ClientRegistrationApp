namespace TravelBookingSystem.Models;

public class Booking
{
    public int Id { get; set; }

    public string BookingNumber { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int TravelId { get; set; }
    public Travel Travel { get; set; } = null!;

    public decimal TotalPrice { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Traveller> Travellers { get; set; }
        = new List<Traveller>();
}