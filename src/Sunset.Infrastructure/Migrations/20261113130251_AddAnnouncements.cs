using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnnouncementId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PromotionalEnabled",
                table: "notification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false),
                    Body = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    AudienceUserIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpectedRecipients = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_announcements_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_AnnouncementId",
                table: "notifications",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_AnnouncementId",
                table: "notifications",
                columns: new[] { "RecipientUserId", "AnnouncementId" },
                unique: true,
                filter: "\"AnnouncementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_CreatedAt",
                table: "announcements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_CreatedByUserId",
                table: "announcements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_Status_CreatedAt",
                table: "announcements",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_announcements_AnnouncementId",
                table: "notifications",
                column: "AnnouncementId",
                principalTable: "announcements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_announcements_AnnouncementId",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropIndex(
                name: "IX_notifications_AnnouncementId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_RecipientUserId_AnnouncementId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "AnnouncementId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "PromotionalEnabled",
                table: "notification_settings");
        }
    }
}
