using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TemplateCode",
                table: "EmailOutboxes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                table: "EmailOutboxes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubjectTemplate = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HtmlBodyTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToRule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CcRecipients = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BccRecipients = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedByUserId = table.Column<int>(type: "int", nullable: true),
                    PublishedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Code_Status",
                table: "EmailTemplates",
                columns: new[] { "Code", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Code_Version",
                table: "EmailTemplates",
                columns: new[] { "Code", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "TemplateCode",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "EmailOutboxes");
        }
    }
}
