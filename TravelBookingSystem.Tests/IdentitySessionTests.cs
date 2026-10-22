using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;
using TravelBookingSystem.Services;
using Xunit;

namespace TravelBookingSystem.Tests;

public sealed class IdentitySessionTests
{
    private static Task<HttpResponseMessage> Register(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Alice", lastName = "Traveller", email = "alice@example.com",
            password = "StrongPass1!", confirmPassword = "StrongPass1!"
        });

    [Fact]
    public async Task Startup_seeds_both_roles_idempotently_without_creating_users()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var seeder = new RoleSeedingService(factory.Services.GetRequiredService<IServiceScopeFactory>());
        await seeder.StartAsync(default);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(new[] { "Admin", "Client" }, await db.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToArrayAsync());
        Assert.False(await db.Users.AnyAsync());
    }

    [Fact]
    public void Role_seed_failure_prevents_application_startup()
    {
        using var factory = new RegistrationFactory(failRoleSeed: true);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("roles could not be initialized", error.Message);
    }

    [Fact]
    public async Task Failed_assignment_result_rolls_back_user_and_preserves_seeded_roles()
    {
        using var factory = new RegistrationFactory(failAssignmentResult: true);
        using var client = factory.CreateClient();
        var response = await Register(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Internal failure", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.UserRoles.AnyAsync());
        Assert.Equal(2, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task Missing_Client_role_is_not_created_during_registration()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.DeleteAsync((await roles.FindByNameAsync("Client"))!)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Register(client)).StatusCode);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Roles.AnyAsync(r => r.Name == "Client"));
    }

    [Fact]
    public void Routes_have_one_registration_and_restore_login_and_refresh()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
            .Select(e => e.RoutePattern.RawText).ToArray();
        Assert.Single(endpoints, p => p == "api/auth/register");
        Assert.Contains("api/auth/login", endpoints);
        Assert.Contains("api/auth/refresh", endpoints);
    }

    [Fact]
    public async Task Unconfirmed_user_can_login_refresh_and_revoked_refresh_is_rejected()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await Register(client)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "alice@example.com", password = "StrongPass1!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", tokens.GetProperty("tokenType").GetString());
        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("accessToken").GetString()));
        var refreshToken = tokens.GetProperty("refreshToken").GetString();
        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.True((await refreshed.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("accessToken", out _));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByEmailAsync("alice@example.com"))!);
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
    }

    [Theory]
    [InlineData("?useCookies=true", true)]
    [InlineData("?useSessionCookies=true", false)]
    public async Task Cookie_login_uses_Identity_cookie(string query, bool persistent)
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        await Register(client);
        var response = await client.PostAsJsonAsync("/api/auth/login" + query,
            new { email = "alice@example.com", password = "StrongPass1!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".AspNetCore.Identity.Application="));
        Assert.Equal(persistent, cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("bad-token")]
    [InlineData("")]
    public async Task Invalid_refresh_is_rejected(string refreshToken)
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
    }

    [Fact]
    public async Task Expired_refresh_is_rejected()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>()
            .Get(IdentityConstants.BearerScheme);
        var token = options.RefreshTokenProtector.Protect(new AuthenticationTicket(
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
            IdentityConstants.BearerScheme));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token })).StatusCode);
    }

    [Fact]
    public async Task Invalid_credentials_and_lockout_are_enforced()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        await Register(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
                new { email = "alice@example.com", password = "WrongPassword1!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "alice@example.com", password = "StrongPass1!" })).StatusCode);
    }
}
