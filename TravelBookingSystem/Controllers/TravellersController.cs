using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;
using TravelBookingSystem.Contracts;


namespace TravelBookingSystem.Controllers;

[ApiController]
[Route("api/bookings/{bookingId:int}/travellers")]
[Authorize(Policy = "CookieSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TravellersController(ApplicationDbContext context)
    : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    public async Task<IActionResult> GetTravellers(int bookingId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
            return Unauthorized();

        var bookingExists = await _context.Bookings
            .AnyAsync(b => b.Id == bookingId && b.UserId == userId);

        if (!bookingExists)
            return NotFound();

        var travellers = await _context.Travellers
            .Where(t => t.BookingId == bookingId)
            .Select(t => new
            {
                t.Id,
                t.FirstName,
                t.LastName,
                t.DateOfBirth
            })
            .ToListAsync();

        return Ok(travellers);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTraveller(
        int bookingId,
        CreateTravellerRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
            return Unauthorized();

        var booking = await _context.Bookings
            .FirstOrDefaultAsync(b =>
                b.Id == bookingId && b.UserId == userId);

        if (booking is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest("First name and last name are required.");
        }

        if (request.DateOfBirth.Date > DateTime.UtcNow.Date)
            return BadRequest("Date of birth cannot be in the future.");

        var traveller = new Traveller
        {
            BookingId = bookingId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth
        };

        _context.Travellers.Add(traveller);

        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetTravellers),
            new { bookingId },
            new
            {
                traveller.Id,
                traveller.FirstName,
                traveller.LastName,
                traveller.DateOfBirth
            });
    }
    [HttpDelete("{travellerId:int}")]
    public async Task<IActionResult> DeleteTraveller(
    int bookingId,
    int travellerId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
            return Unauthorized();

        var bookingExists = await _context.Bookings
            .AnyAsync(b => b.Id == bookingId && b.UserId == userId);

        if (!bookingExists)
            return NotFound();

        var traveller = await _context.Travellers
            .FirstOrDefaultAsync(t =>
                t.Id == travellerId && t.BookingId == bookingId);

        if (traveller is null)
            return NotFound();

        _context.Travellers.Remove(traveller);

        await _context.SaveChangesAsync();

        return NoContent();
    }
    [HttpPut("{travellerId:int}")]
    public async Task<IActionResult> UpdateTraveller(
    int bookingId,
    int travellerId,
    UpdateTravellerRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
            return Unauthorized();

        var bookingExists = await _context.Bookings
            .AnyAsync(b => b.Id == bookingId && b.UserId == userId);

        if (!bookingExists)
            return NotFound();

        var traveller = await _context.Travellers
            .FirstOrDefaultAsync(t =>
                t.Id == travellerId && t.BookingId == bookingId);

        if (traveller is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest("First name and last name are required.");
        }

        if (request.DateOfBirth.Date > DateTime.UtcNow.Date)
            return BadRequest("Date of birth cannot be in the future.");

        traveller.FirstName = request.FirstName.Trim();
        traveller.LastName = request.LastName.Trim();
        traveller.DateOfBirth = request.DateOfBirth;

        await _context.SaveChangesAsync();

        return NoContent();
    }
}