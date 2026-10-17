using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelBookingSystem.Models;

namespace TravelBookingSystem.Data;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Travel> Travels => Set<Travel>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Traveller> Travellers => Set<Traveller>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Travel>()
            .Property(t => t.PricePerPerson)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Booking>()
            .Property(b => b.TotalPrice)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Booking>()
            .HasIndex(b => b.BookingNumber)
            .IsUnique();

    }
}