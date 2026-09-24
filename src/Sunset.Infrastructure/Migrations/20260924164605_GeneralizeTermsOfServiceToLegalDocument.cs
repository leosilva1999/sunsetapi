using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeTermsOfServiceToLegalDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: the EF-scaffolded version was a DropTable + CreateTable, which would
            // have destroyed any already-published terms of service. This renames the table in
            // place instead and backfills DocumentType = 0 (TermsOfService) for existing rows -
            // the only kind of document that could have existed before this migration.
            migrationBuilder.RenameTable(
                name: "terms_of_service",
                newName: "legal_documents");

            migrationBuilder.RenameIndex(
                table: "legal_documents",
                name: "IX_terms_of_service_UpdatedByUserId",
                newName: "IX_legal_documents_UpdatedByUserId");

            migrationBuilder.DropIndex(
                name: "IX_terms_of_service_Version",
                table: "legal_documents");

            migrationBuilder.AddColumn<int>(
                name: "DocumentType",
                table: "legal_documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Versions are now numbered per document type rather than globally.
            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_DocumentType_Version",
                table: "legal_documents",
                columns: new[] { "DocumentType", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_legal_documents_DocumentType_Version",
                table: "legal_documents");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "legal_documents");

            migrationBuilder.RenameIndex(
                table: "legal_documents",
                name: "IX_legal_documents_UpdatedByUserId",
                newName: "IX_terms_of_service_UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_terms_of_service_Version",
                table: "legal_documents",
                column: "Version",
                unique: true);

            migrationBuilder.RenameTable(
                name: "legal_documents",
                newName: "terms_of_service");
        }
    }
}
