using Microsoft.AspNetCore.Identity;

namespace TravelBookingSystem.Services;

// Hosted services start before the HTTP server accepts requests.
public sealed class RoleSeedingService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { "Client", "Admin" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await roles.RoleExistsAsync(name)) continue;
            var result = await roles.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded)
                throw new InvalidOperationException("Required application roles could not be initialized.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
