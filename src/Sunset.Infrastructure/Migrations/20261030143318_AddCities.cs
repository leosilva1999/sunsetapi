using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AlternateNames = table.Column<string>(type: "text", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    AdminArea = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<long>(type: "bigint", nullable: true),
                    NationalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ParentCityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Population = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', \"SearchText\")", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cities_cities_ParentCityId",
                        column: x => x.ParentCityId,
                        principalTable: "cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cities_Country_AdminArea_Name",
                table: "cities",
                columns: new[] { "Country", "AdminArea", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_cities_IsActive_Kind_Population",
                table: "cities",
                columns: new[] { "IsActive", "Kind", "Population" });

            migrationBuilder.CreateIndex(
                name: "IX_cities_NationalCode",
                table: "cities",
                column: "NationalCode");

            migrationBuilder.CreateIndex(
                name: "IX_cities_ParentCityId",
                table: "cities",
                column: "ParentCityId");

            migrationBuilder.CreateIndex(
                name: "IX_cities_SearchVector",
                table: "cities",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_cities_Slug",
                table: "cities",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cities_Source_ExternalId",
                table: "cities",
                columns: new[] { "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cities");
        }
    }
}
