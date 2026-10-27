using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;
using Xunit;

namespace TravelBookingSystem.Tests;

public sealed class LogoutTests
{
    private static HttpClient Client(RegistrationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task Register(HttpClient client)
    {
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Alice", lastName = "Traveller", email = "alice@example.com",
            password = "StrongPass1!", confirmPassword = "StrongPass1!"
        })).StatusCode);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string query) =>
        client.PostAsJsonAsync("/api/auth/login" + query, new { email = "alice@example.com", password = "StrongPass1!" });

    private static async Task<string> Token(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!;
    }

    private static Task<HttpResponseMessage> Logout(HttpClient client, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return client.SendAsync(request);
    }

    [Theory]
    [InlineData("?useSessionCookies=true")]
    [InlineData("?useCookies=true")]
    public async Task Logout_expires_cookie_and_me_returns_401(string query)
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var token = await Token(client);
        var response = await Logout(client, token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c =>
            c.StartsWith(".AspNetCore.Identity.Application=;") && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task Missing_or_invalid_csrf_token_does_not_sign_out(string? token)
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Login(client, "?useSessionCookies=true");
        await Token(client);
        var response = await Logout(client, token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Token_from_another_cookie_jar_is_rejected()
    {
        using var factory = new RegistrationFactory();
        using var first = Client(factory);
        using var second = Client(factory);
        await Register(first);
        await Login(first, "?useSessionCookies=true");
        await Login(second, "?useSessionCookies=true");
        var wrongToken = await Token(first);
        await Token(second);
        Assert.Equal(HttpStatusCode.BadRequest, (await Logout(second, wrongToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Bearer_only_and_anonymous_requests_cannot_use_cookie_logout()
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Logout(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/antiforgery")).StatusCode);
        await Register(client);
        var login = await Login(client, "");
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Logout(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/antiforgery")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cookie_logout_does_not_revoke_bearer_tokens_or_modify_database_records()
    {
        using var factory = new RegistrationFactory();
        using var browser = Client(factory);
        using var bearer = Client(factory);
        await Register(browser);
        var tokens = await (await Login(bearer, "")).Content.ReadFromJsonAsync<JsonElement>();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        await Login(browser, "?useSessionCookies=true");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync();
            var travel = new Travel { Title = "Test trip", Destination = "Riga", Capacity = 10 };
            db.Bookings.Add(new Booking
            {
                BookingNumber = "LOGOUT-TEST", User = user, Travel = travel, TotalPrice = 100,
                Travellers = new List<Traveller> { new() { FirstName = "Test", LastName = "Traveller" } }
            });
            await db.SaveChangesAsync();
        }
        var before = await Snapshot(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await Logout(browser, await Token(browser))).StatusCode);
        Assert.Equal(before, await Snapshot(factory));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bearer.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokens.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    private static async Task<string> Snapshot(RegistrationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Read every mapped scalar property, including account stamps and password hash.
        var tables = new object[]
        {
            await db.Users.AsNoTracking().ToArrayAsync(), await db.Roles.AsNoTracking().ToArrayAsync(),
            await db.UserRoles.AsNoTracking().ToArrayAsync(), await db.Travels.AsNoTracking().ToArrayAsync(),
            await db.Bookings.AsNoTracking().ToArrayAsync(), await db.Travellers.AsNoTracking().ToArrayAsync(),
            await db.UserClaims.AsNoTracking().ToArrayAsync(), await db.UserLogins.AsNoTracking().ToArrayAsync(),
            await db.UserTokens.AsNoTracking().ToArrayAsync(), await db.RoleClaims.AsNoTracking().ToArrayAsync()
        };
        return JsonSerializer.Serialize(tables);
    }

    [Fact]
    public void Exactly_one_logout_post_exists()
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        Assert.Single(routes, e => e.RoutePattern.RawText == "api/auth/logout" &&
            e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));
    }
}
