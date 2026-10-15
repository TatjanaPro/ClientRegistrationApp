using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelBookingSystem.Data;
using TravelBookingSystem.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure MySQL database
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Database connection string not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 1, 0))
    )
);

// Configure ASP.NET Core Identity
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();

app.Run();
