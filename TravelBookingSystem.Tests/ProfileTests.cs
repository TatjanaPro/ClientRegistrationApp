using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;
using Xunit;

namespace TravelBookingSystem.Tests;

public sealed class ProfileTests
{
    private static HttpClient Client(RegistrationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static Dictionary<string, object?> Update() => new()
    {
        ["firstName"] = "Changed", ["lastName"] = "Profile", ["email"] = "new@example.com", ["phoneNumber"] = "+37120000000"
    };

    private static async Task Register(HttpClient client, string email = "alice@example.com")
    {
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Alice", lastName = "Traveller", email,
            password = "StrongPass1!", confirmPassword = "StrongPass1!"
        })).StatusCode);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email = "alice@example.com", string query = "?useSessionCookies=true") =>
        client.PostAsJsonAsync("/api/auth/login" + query, new { email, password = "StrongPass1!" });

    private static async Task<string> Token(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, Dictionary<string, object?> body, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me") { Content = JsonContent.Create(body) };
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return client.SendAsync(request);
    }

    private static async Task<ApplicationUser> User(RegistrationFactory factory, string email = "alice@example.com")
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Email == email);
    }

    [Theory]
    [InlineData("?useSessionCookies=true")]
    [InlineData("?useCookies=true")]
    public async Task Update_normalizes_email_preserves_credentials_refreshes_cookie_and_allows_logout(string query)
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Login(client, query: query);
        var before = await User(factory);
        var body = Update();
        body["firstName"] = " Changed ";
        body["email"] = "New@Example.com";
        var response = await Put(client, body, await Token(client));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".AspNetCore.Identity.Application="));
        var after = await User(factory, "New@Example.com");
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal("NEW@EXAMPLE.COM", after.NormalizedEmail);
        Assert.Equal("NEW@EXAMPLE.COM", after.NormalizedUserName);
        Assert.Equal("New@Example.com", after.UserName);
        Assert.Equal("Changed", after.FirstName);
        Assert.Equal("Profile", after.LastName);
        Assert.Equal("+37120000000", after.PhoneNumber);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("+37120000000", profile.GetProperty("phoneNumber").GetString());
        Assert.Equal("Changed", profile.GetProperty("firstName").GetString());
        Assert.Equal("New@Example.com", profile.GetProperty("email").GetString());
        Assert.DoesNotContain("passwordHash", await me.Content.ReadAsStringAsync());

        using var fresh = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(fresh)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(fresh, "new@example.com")).StatusCode);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", await Token(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_does_not_partially_save_profile()
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Register(client, "other@example.com");
        await Login(client);
        var before = JsonSerializer.Serialize(await User(factory));
        var body = Update();
        body["email"] = "OTHER@example.com";
        Assert.Equal(HttpStatusCode.Conflict, (await Put(client, body, await Token(client))).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(await User(factory)));
    }

    [Fact]
    public async Task Same_email_is_allowed_and_optional_phone_can_be_cleared()
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Login(client);
        var body = Update();
        body["email"] = "alice@example.com";
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, body, await Token(client))).StatusCode);
        body.Remove("phoneNumber");
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, body, await Token(client))).StatusCode);
        Assert.Null((await User(factory)).PhoneNumber);
    }

    [Fact]
    public async Task Request_cannot_target_another_user_or_change_sensitive_fields()
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Register(client, "other@example.com");
        var other = await User(factory, "other@example.com");
        var otherBefore = JsonSerializer.Serialize(other);
        await Login(client);
        var before = await User(factory);
        var body = Update();
        body["userId"] = other.Id;
        body["id"] = other.Id;
        body["roles"] = new[] { "Admin" };
        body["passwordHash"] = "attacker";
        body["securityStamp"] = "attacker";
        body["emailConfirmed"] = true;
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, body, await Token(client))).StatusCode);
        Assert.Equal(otherBefore, JsonSerializer.Serialize(await User(factory, "other@example.com")));
        var after = await User(factory, "new@example.com");
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.False(after.EmailConfirmed);
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(new[] { "Client" }, await manager.GetRolesAsync(after));
    }

    [Theory]
    [InlineData("firstName", "")]
    [InlineData("firstName", "   ")]
    [InlineData("lastName", "")]
    [InlineData("email", "")]
    [InlineData("email", "invalid")]
    [InlineData("phoneNumber", "invalid")]
    public async Task Invalid_fields_are_rejected(string field, string value)
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Login(client);
        var before = JsonSerializer.Serialize(await User(factory));
        var body = Update();
        body[field] = value;
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, body, await Token(client))).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(await User(factory)));
    }

    [Theory]
    [InlineData("firstName", 101)]
    [InlineData("lastName", 101)]
    [InlineData("email", 257)]
    [InlineData("phoneNumber", 31)]
    public async Task Excessive_lengths_are_rejected(string field, int length)
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Login(client);
        var body = Update();
        body[field] = new string('a', length);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, body, await Token(client))).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad-token")]
    public async Task Missing_or_invalid_csrf_prevents_changes(string? token)
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        await Register(client);
        await Login(client);
        await Token(client);
        var before = JsonSerializer.Serialize(await User(factory));
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, Update(), token)).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(await User(factory)));
    }

    [Fact]
    public async Task Anonymous_and_bearer_only_requests_are_rejected()
    {
        using var factory = new RegistrationFactory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Put(client, Update(), null)).StatusCode);
        await Register(client);
        var tokens = await (await Login(client, query: "")).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Put(client, Update(), null)).StatusCode);
    }

    [Fact]
    public async Task Database_failure_returns_safe_error_without_partial_update()
    {
        using var factory = new RegistrationFactory(failProfileUpdate: true);
        using var client = Client(factory);
        await Register(client);
        await Login(client);
        var before = JsonSerializer.Serialize(await User(factory));
        var response = await Put(client, Update(), await Token(client));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Simulated", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, JsonSerializer.Serialize(await User(factory)));
    }
}

internal sealed class FailProfileUpdateInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<ApplicationUser>()
            .Any(e => e.State == EntityState.Modified && e.Entity.FirstName == "Changed"))
            throw new DbUpdateException("Simulated internal database failure");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
