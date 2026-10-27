using System.Data.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using TravelBookingSystem.Contracts;
using TravelBookingSystem.Models;

namespace TravelBookingSystem.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = "CookieSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "A valid antiforgery token is required.");
        }

        try
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var email = request.Email.Trim();
            var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                user.EmailConfirmed = false;
            if (user.PhoneNumber != phone) user.PhoneNumberConfirmed = false;

            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName.Trim();
            user.Email = email;
            user.UserName = email;
            user.PhoneNumber = phone;

            // One Identity update validates uniqueness, normalizes email/username and
            // saves all profile fields together. No intermediate email-only save.
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                    return EmailConflict();
                if (result.Errors.Any(e => e.Code == "ConcurrencyFailure"))
                    return Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "Your profile changed. Reload it and try again.");

                return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["Profile"] = ["Profile could not be updated. Check your details."]
                }));
            }

            await signInManager.RefreshSignInAsync(user);
            return NoContent();
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: 1062 })
        {
            // Concurrent email changes are also guarded by the existing unique username index.
            return EmailConflict();
        }
        catch (DbUpdateException)
        {
            return UpdateUnavailable();
        }
        catch (DbException)
        {
            return UpdateUnavailable();
        }
    }

    private ObjectResult EmailConflict() => Problem(statusCode: StatusCodes.Status409Conflict,
        title: "An account with this email already exists.");

    private ObjectResult UpdateUnavailable() => Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Profile updates are temporarily unavailable. Please try again.");
}
