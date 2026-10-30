using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationCityLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CityId",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocalityId",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_locations_CityId",
                table: "locations",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_CreatedByUserId",
                table: "locations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_LocalityId",
                table: "locations",
                column: "LocalityId");

            migrationBuilder.AddForeignKey(
                name: "FK_locations_cities_CityId",
                table: "locations",
                column: "CityId",
                principalTable: "cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_cities_LocalityId",
                table: "locations",
                column: "LocalityId",
                principalTable: "cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_users_CreatedByUserId",
                table: "locations",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_locations_cities_CityId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_cities_LocalityId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_users_CreatedByUserId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_CityId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_CreatedByUserId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_LocalityId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "LocalityId",
                table: "locations");
        }
    }
}
