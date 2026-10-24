using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelBookingSystem.Contracts;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;

namespace TravelBookingSystem.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ApplicationDbContext dbContext,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string ClientRole = "Client";

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null : request.PhoneNumber.Trim()
        };

        try
        {
            // Identity saves each operation separately; the transaction makes registration atomic.
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            if (!await roleManager.RoleExistsAsync(ClientRole))
                return RegistrationUnavailable();

            var creationResult = await userManager.CreateAsync(user, request.Password);
            if (!creationResult.Succeeded)
            {
                return RegistrationErrors(creationResult);
            }

            // The request has no role field. Public registration always assigns Client.
            var assignmentResult = await userManager.AddToRoleAsync(user, ClientRole);
            if (!assignmentResult.Succeeded)
            {
                return RegistrationUnavailable();
            }

            await transaction.CommitAsync();
            return StatusCode(StatusCodes.Status201Created,
                new { message = "Registration successful." });
        }
        catch (DbUpdateException)
        {
            return RegistrationUnavailable();
        }
        catch (DbException)
        {
            return RegistrationUnavailable();
        }
    }

    private IActionResult RegistrationErrors(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "An account with this email already exists.");
        }

        // Only known, safe messages are returned; never serialize Identity users or exceptions.
        var messages = result.Errors.Select(error => error.Code switch
        {
            "PasswordTooShort" => "Password must contain at least 8 characters.",
            "PasswordRequiresDigit" => "Password must contain a digit.",
            "PasswordRequiresLower" => "Password must contain a lowercase letter.",
            "PasswordRequiresUpper" => "Password must contain an uppercase letter.",
            "PasswordRequiresNonAlphanumeric" => "Password must contain a special character.",
            "InvalidEmail" or "InvalidUserName" => "Email is invalid.",
            _ => "Registration could not be completed. Check your details."
        }).Distinct().ToArray();

        return BadRequest(new ValidationProblemDetails(
            new Dictionary<string, string[]> { ["Registration"] = messages }));
    }

    private ObjectResult RegistrationUnavailable()
    {
        logger.LogWarning("Registration could not be saved or the Client role could not be assigned.");
        return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Registration is temporarily unavailable. Please try again.");
    }
}
