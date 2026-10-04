using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAdministrationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailOutboxes_PurchaseOrderId",
                table: "EmailOutboxes");

            migrationBuilder.AddColumn<string>(
                name: "BccRecipients",
                table: "EmailOutboxes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CcRecipients",
                table: "EmailOutboxes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByName",
                table: "EmailOutboxes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "EmailOutboxes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CreatedDate",
                table: "EmailOutboxes",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "FromAddress",
                table: "EmailOutboxes",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FromName",
                table: "EmailOutboxes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ResentFromEmailOutboxId",
                table: "EmailOutboxes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "EmailOutboxes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "EmailOutboxes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "EmailOutboxes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Defer compilation until the new columns exist, including in idempotent scripts.
            migrationBuilder.Sql(
                """
                EXEC(N'
                UPDATE email
                SET SourceType = ''PurchaseOrder'',
                    SourceId = email.PurchaseOrderId,
                    SourceReference = purchaseOrder.PurchaseOrderNumber,
                    FromAddress = ''purchasing@project1.local'',
                    FromName = ''Project1 Purchasing'',
                    CreatedByUserId = COALESCE(purchaseOrder.IssuedByUserId, purchaseOrder.CreatedByUserId),
                    CreatedByName = COALESCE(purchaseOrder.IssuedByName, purchaseOrder.CreatedByName),
                    CreatedDate = CONVERT(date, email.CreatedAtUtc)
                FROM EmailOutboxes AS email
                INNER JOIN PurchaseOrders AS purchaseOrder
                    ON purchaseOrder.Id = email.PurchaseOrderId;
                ');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxes_CreatedDate_Id",
                table: "EmailOutboxes",
                columns: new[] { "CreatedDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxes_PurchaseOrderId",
                table: "EmailOutboxes",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxes_SourceReference",
                table: "EmailOutboxes",
                column: "SourceReference");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxes_SourceType_SourceId",
                table: "EmailOutboxes",
                columns: new[] { "SourceType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH DuplicateEmails AS
                (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY PurchaseOrderId
                               ORDER BY Id DESC) AS RowNumber
                    FROM EmailOutboxes
                )
                DELETE FROM DuplicateEmails WHERE RowNumber > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_EmailOutboxes_CreatedDate_Id",
                table: "EmailOutboxes");

            migrationBuilder.DropIndex(
                name: "IX_EmailOutboxes_PurchaseOrderId",
                table: "EmailOutboxes");

            migrationBuilder.DropIndex(
                name: "IX_EmailOutboxes_SourceReference",
                table: "EmailOutboxes");

            migrationBuilder.DropIndex(
                name: "IX_EmailOutboxes_SourceType_SourceId",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "BccRecipients",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "CcRecipients",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "CreatedByName",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "FromAddress",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "FromName",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "ResentFromEmailOutboxId",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "EmailOutboxes");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "EmailOutboxes");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxes_PurchaseOrderId",
                table: "EmailOutboxes",
                column: "PurchaseOrderId",
                unique: true);
        }
    }
}
