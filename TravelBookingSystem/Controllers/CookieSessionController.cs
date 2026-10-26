using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TravelBookingSystem.Models;

namespace TravelBookingSystem.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize(Policy = "CookieSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CookieSessionController(
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("antiforgery")]
    public IActionResult AntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
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

        await signInManager.SignOutAsync();
        return NoContent();
    }
}
