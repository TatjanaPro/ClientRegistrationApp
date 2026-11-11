using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelBookingSystem.Migrations
{
    public partial class AddPersonCountToBooking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PersonCount",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonCount",
                table: "Bookings");
        }
    }
}