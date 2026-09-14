using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationFullTextSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_locations_Name_City",
                table: "locations",
                columns: new[] { "Name", "City" })
                .Annotation("MySql:FullTextIndex", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_locations_Name_City",
                table: "locations");
        }
    }
}
