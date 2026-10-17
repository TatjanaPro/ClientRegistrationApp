namespace TravelBookingSystem.Models;

public class Travel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string TravelType { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal PricePerPerson { get; set; }
    public int Capacity { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Booking> Bookings { get; set; }
        = new List<Booking>();
}