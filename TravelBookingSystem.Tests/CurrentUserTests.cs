using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelBookingSystem.Models;
using Xunit;

namespace TravelBookingSystem.Tests;

public sealed class CurrentUserTests
{
    private static HttpClient CreateClient(RegistrationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static Task<HttpResponseMessage> Register(HttpClient client) => client.PostAsJsonAsync("/api/auth/register", new
    {
        firstName = "Alice", lastName = "Traveller", email = "alice@example.com",
        password = "StrongPass1!", confirmPassword = "StrongPass1!"
    });

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task Me_rejects_unauthenticated_requests_without_redirect(string? token)
    {
        using var factory = new RegistrationFactory();
        using var client = CreateClient(factory);
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("?useSessionCookies=true", false)]
    [InlineData("?useCookies=true", false)]
    [InlineData("", true)]
    [InlineData("?useSessionCookies=true", true)]
    public async Task Login_preserves_roles_and_me_returns_only_safe_profile_fields(string query, bool addAdmin)
    {
        using var factory = new RegistrationFactory();
        using var client = CreateClient(factory);
        Assert.Equal(HttpStatusCode.Created, (await Register(client)).StatusCode);
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("alice@example.com"))!;
            userId = user.Id;
            Assert.False(user.EmailConfirmed);
            // Only test setup grants Admin, never public registration or login.
            if (addAdmin) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login" + query,
            new { email = "ALICE@example.com", password = "StrongPass1!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        System.Security.Claims.ClaimsPrincipal principal;
        if (query.Length == 0)
        {
            var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
            var accessToken = tokens.GetProperty("accessToken").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var options = factory.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
            principal = options.BearerTokenProtector.Unprotect(accessToken)!.Principal;
        }
        else
        {
            var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Identity.Application="));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
            var ticket = cookie.Split(';')[0].Split('=', 2)[1];
            var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            principal = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(ticket))!.Principal;
        }
        Assert.True(principal.IsInRole("Client"));
        Assert.Equal(addAdmin, principal.IsInRole("Admin"));

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "email", "firstName", "id", "lastName", "phoneNumber", "roles" },
            profile.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(userId, profile.GetProperty("id").GetString());
        Assert.Equal("Alice", profile.GetProperty("firstName").GetString());
        Assert.Equal("Traveller", profile.GetProperty("lastName").GetString());
        Assert.Equal("alice@example.com", profile.GetProperty("email").GetString());
        Assert.Equal(addAdmin ? new[] { "Admin", "Client" } : new[] { "Client" },
            profile.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).OrderBy(r => r).ToArray());
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_have_identical_safe_responses()
    {
        using var factory = new RegistrationFactory();
        using var client = CreateClient(factory);
        await Register(client);
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email = "alice@example.com", password = "Wrong1!" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new { email = "unknown@example.com", password = "Wrong1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        // ProblemDetails includes a different diagnostic trace ID for each request.
        var wrongBody = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknownUser.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "status", "title", "traceId", "type" },
            wrongBody.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(new[] { "status", "title", "traceId", "type" },
            unknownBody.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        foreach (var field in new[] { "type", "title", "status" })
            Assert.Equal(wrongBody.GetProperty(field).ToString(), unknownBody.GetProperty(field).ToString());
        Assert.DoesNotContain("Wrong1!", await wrongPassword.Content.ReadAsStringAsync());
    }
}
