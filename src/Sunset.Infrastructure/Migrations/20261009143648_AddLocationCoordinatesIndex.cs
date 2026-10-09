using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationCoordinatesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_locations_Latitude_Longitude",
                table: "locations",
                columns: new[] { "Latitude", "Longitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_locations_Latitude_Longitude",
                table: "locations");
        }
    }
}
