using System.ComponentModel.DataAnnotations;

namespace TravelBookingSystem.Models;

public class Travel
{
    public int Id { get; set; }
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(300)]
    public string Destination { get; set; } = string.Empty;
    [MaxLength(1500)]
    public string Description { get; set; } = string.Empty;
    [MaxLength(200)]
    public string TravelType { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal PricePerPerson { get; set; }
    public int Capacity { get; set; }
    [MaxLength(400)]
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Booking> Bookings { get; set; }
        = new List<Booking>();
}