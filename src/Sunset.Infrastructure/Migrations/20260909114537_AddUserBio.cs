using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "users",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bio",
                table: "users");
        }
    }
}
