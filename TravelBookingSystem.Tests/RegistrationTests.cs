using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;
using Xunit;

namespace TravelBookingSystem.Tests;

public sealed class RegistrationTests
{
    private static Dictionary<string, object?> ValidRequest() => new()
    {
        ["firstName"] = "Alice",
        ["lastName"] = "Traveller",
        ["email"] = "alice@example.com",
        ["password"] = "StrongPass1!",
        ["confirmPassword"] = "StrongPass1!"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("+37120000000")]
    public async Task Registration_stores_profile_and_hashed_password_with_only_Client_role(string? phone)
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var request = ValidRequest();
        request["phoneNumber"] = phone;
        request["role"] = "Admin";
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StrongPass1!", body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = Assert.IsType<ApplicationUser>(await manager.FindByEmailAsync("alice@example.com"));
        Assert.Equal("Alice", user.FirstName);
        Assert.Equal("Traveller", user.LastName);
        Assert.Equal(phone, user.PhoneNumber);
        Assert.False(user.EmailConfirmed);
        Assert.NotEqual("StrongPass1!", user.PasswordHash);
        Assert.True(await manager.CheckPasswordAsync(user, "StrongPass1!"));
        Assert.Equal(new[] { "Client" }, await manager.GetRolesAsync(user));
        var options = manager.Options;
        Assert.True(options.User.RequireUniqueEmail);
        Assert.False(options.SignIn.RequireConfirmedEmail);
        Assert.False(options.SignIn.RequireConfirmedAccount);
    }

    [Theory]
    [InlineData("firstName", "")]
    [InlineData("lastName", "   ")]
    [InlineData("email", "not-an-email")]
    [InlineData("email", "")]
    [InlineData("password", "")]
    [InlineData("confirmPassword", "")]
    [InlineData("confirmPassword", "Different1!")]
    [InlineData("phoneNumber", "invalid")]
    public async Task Invalid_fields_are_rejected_without_creating_users(string field, string value)
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var request = ValidRequest();
        request[field] = value;
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync());
    }

    [Theory]
    [InlineData("firstName", 101)]
    [InlineData("lastName", 101)]
    [InlineData("email", 257)]
    [InlineData("phoneNumber", 31)]
    [InlineData("password", 129)]
    public async Task Excessive_lengths_are_rejected(string field, int length)
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var request = ValidRequest();
        request[field] = new string('a', length);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
    }

    [Theory]
    [InlineData("Ab1!abc")]
    [InlineData("weakpassword")]
    [InlineData("StrongPassword!")]
    [InlineData("strongpass1!")]
    [InlineData("STRONGPASS1!")]
    [InlineData("StrongPass12")]
    public async Task Password_policy_is_enforced(string password)
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var request = ValidRequest();
        request["password"] = request["confirmPassword"] = password;
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(password, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Email_uniqueness_is_case_insensitive()
    {
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        var request = ValidRequest();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        request["email"] = "ALICE@example.com";
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Failed_role_assignment_rolls_back_registration()
    {
        using var factory = new RegistrationFactory(failRoleAssignment: true);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRequest());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Simulated", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.UserRoles.AnyAsync());
    }
}

internal sealed class RegistrationFactory(bool failRoleAssignment = false,
    bool failAssignmentResult = false, bool failRoleSeed = false) : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            if (failAssignmentResult)
                services.AddScoped<IUserValidator<ApplicationUser>, FailAssignmentValidator>();
            if (failRoleSeed)
                services.AddScoped<IRoleValidator<IdentityRole>, FailRoleValidator>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            connection.Open();
            // Create only the isolated SQLite schema before the startup role seeder runs.
            using (var setup = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options))
                setup.Database.EnsureCreated();
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(connection);
                if (failRoleAssignment)
                    options.AddInterceptors(new FailRoleAssignmentInterceptor());
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Unused test connection" }));
        var host = base.CreateHost(builder);
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

internal sealed class FailAssignmentValidator : IUserValidator<ApplicationUser>
{
    private int calls;
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        => Task.FromResult(++calls == 2
            ? IdentityResult.Failed(new IdentityError { Code = "Simulated", Description = "Internal failure" })
            : IdentityResult.Success);
}

internal sealed class FailRoleValidator : IRoleValidator<IdentityRole>
{
    public Task<IdentityResult> ValidateAsync(RoleManager<IdentityRole> manager, IdentityRole role)
        => Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Simulated" }));
}

internal sealed class FailRoleAssignmentInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<IdentityUserRole<string>>()
            .Any(entry => entry.State == EntityState.Added))
            throw new DbUpdateException("Simulated role assignment failure.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
