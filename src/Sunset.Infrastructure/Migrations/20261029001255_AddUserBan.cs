using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BanReason",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BannedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BannedByUserId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BannedUntil",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BanReason",
                table: "users");

            migrationBuilder.DropColumn(
                name: "BannedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "BannedByUserId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "BannedUntil",
                table: "users");
        }
    }
}
