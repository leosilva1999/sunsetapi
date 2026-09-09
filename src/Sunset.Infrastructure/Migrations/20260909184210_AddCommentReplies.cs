using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunset.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentCommentId",
                table: "comments",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "RepliesCount",
                table: "comments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_comments_ParentCommentId",
                table: "comments",
                column: "ParentCommentId");

            migrationBuilder.AddForeignKey(
                name: "FK_comments_comments_ParentCommentId",
                table: "comments",
                column: "ParentCommentId",
                principalTable: "comments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_comments_comments_ParentCommentId",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "IX_comments_ParentCommentId",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "RepliesCount",
                table: "comments");
        }
    }
}
