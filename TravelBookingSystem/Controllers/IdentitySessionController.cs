using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TravelBookingSystem.Models;

namespace TravelBookingSystem.Controllers;

// Uses the same Identity handlers and request contracts as MapIdentityApi,
// exposing only login and refresh alongside the custom registration endpoint.
[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class IdentitySessionController(
    SignInManager<ApplicationUser> signInManager,
    IOptionsMonitor<BearerTokenOptions> bearerOptions,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request,
        [FromQuery] bool? useCookies, [FromQuery] bool? useSessionCookies)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return Unauthorized();

        var cookies = useCookies == true || useSessionCookies == true;
        signInManager.AuthenticationScheme = cookies
            ? IdentityConstants.ApplicationScheme : IdentityConstants.BearerScheme;
        var persistent = useCookies == true && useSessionCookies != true;
        var result = await signInManager.PasswordSignInAsync(
            request.Email.Trim(), request.Password, persistent, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            if (!string.IsNullOrEmpty(request.TwoFactorCode))
                result = await signInManager.TwoFactorAuthenticatorSignInAsync(
                    request.TwoFactorCode, persistent, rememberClient: persistent);
            else if (!string.IsNullOrEmpty(request.TwoFactorRecoveryCode))
                result = await signInManager.TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
        }

        // The Identity handler writes the cookie or bearer-token response on success.
        return result.Succeeded ? new EmptyResult() : Unauthorized();
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        if (string.IsNullOrEmpty(request.RefreshToken)) return Unauthorized();
        var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || timeProvider.GetUtcNow() >= expires ||
            await signInManager.ValidateSecurityStampAsync(ticket.Principal) is not ApplicationUser user)
            return Unauthorized();

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
