using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "photos",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "photos",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "comments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "comments",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "moderation_actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModeratorId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    TargetDescription = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moderation_actions_users_ModeratorId",
                        column: x => x.ModeratorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReporterId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Details = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reports_users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "terms_of_service",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Content = table.Column<string>(type: "mediumtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_terms_of_service", x => x.Id);
                    table.ForeignKey(
                        name: "FK_terms_of_service_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_photos_DeletedAt",
                table: "photos",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_photos_DeletedByUserId",
                table: "photos",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_comments_DeletedAt",
                table: "comments",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_comments_DeletedByUserId",
                table: "comments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_actions_CreatedAt",
                table: "moderation_actions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_actions_ModeratorId",
                table: "moderation_actions",
                column: "ModeratorId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_ReporterId_TargetType_TargetId",
                table: "reports",
                columns: new[] { "ReporterId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reports_ResolvedByUserId",
                table: "reports",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_Status",
                table: "reports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_terms_of_service_UpdatedByUserId",
                table: "terms_of_service",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_terms_of_service_Version",
                table: "terms_of_service",
                column: "Version",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_users_DeletedByUserId",
                table: "comments",
                column: "DeletedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_photos_users_DeletedByUserId",
                table: "photos",
                column: "DeletedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_comments_users_DeletedByUserId",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_photos_users_DeletedByUserId",
                table: "photos");

            migrationBuilder.DropTable(
                name: "moderation_actions");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "terms_of_service");

            migrationBuilder.DropIndex(
                name: "IX_photos_DeletedAt",
                table: "photos");

            migrationBuilder.DropIndex(
                name: "IX_photos_DeletedByUserId",
                table: "photos");

            migrationBuilder.DropIndex(
                name: "IX_comments_DeletedAt",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "IX_comments_DeletedByUserId",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "comments");
        }
    }
}
