using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "locations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Locations that already exist get a slug now: their name (accents stripped) plus the first 8 hex digits of
            // their id, which keeps them unique. Locations registered from here on get "{name}-{city slug}" from the API.
            migrationBuilder.Sql(@"
                UPDATE locations
                SET ""Slug"" =
                    COALESCE(
                        NULLIF(
                            trim(both '-' from regexp_replace(
                                lower(translate(""Name"",
                                    'áàâãäåéèêëíìîïóòôõöúùûüçñÁÀÂÃÄÅÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑ',
                                    'aaaaaaeeeeiiiiooooouuuucnAAAAAAEEEEIIIIOOOOOUUUUCN')),
                                '[^a-z0-9]+', '-', 'g')),
                            ''),
                        'local')
                    || '-' || substr(replace(""Id""::text, '-', ''), 1, 8)
                WHERE ""Slug"" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_locations_Slug",
                table: "locations",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_locations_Slug",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "locations");
        }
    }
}
