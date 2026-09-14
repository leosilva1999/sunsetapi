using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CascadeDeleteRatingsOnLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ratings_locations_LocationId",
                table: "ratings");

            migrationBuilder.AddForeignKey(
                name: "FK_ratings_locations_LocationId",
                table: "ratings",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ratings_locations_LocationId",
                table: "ratings");

            migrationBuilder.AddForeignKey(
                name: "FK_ratings_locations_LocationId",
                table: "ratings",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
