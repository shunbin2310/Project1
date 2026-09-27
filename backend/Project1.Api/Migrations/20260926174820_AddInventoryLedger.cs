using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoodsReceiptItemId = table.Column<int>(type: "int", nullable: true),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: false),
                    PerformedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_GoodsReceiptItems_GoodsReceiptItemId",
                        column: x => x.GoodsReceiptItemId,
                        principalTable: "GoodsReceiptItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_ProductId",
                table: "InventoryBalances",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_GoodsReceiptItemId",
                table: "InventoryTransactions",
                column: "GoodsReceiptItemId",
                unique: true,
                filter: "[GoodsReceiptItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ProductId_OccurredDate_Id",
                table: "InventoryTransactions",
                columns: new[] { "ProductId", "OccurredDate", "Id" });

            migrationBuilder.Sql(
                """
                INSERT INTO [InventoryTransactions]
                (
                    [ProductId],
                    [Type],
                    [QuantityChange],
                    [QuantityBefore],
                    [QuantityAfter],
                    [ProductCode],
                    [ProductName],
                    [UnitOfMeasureCode],
                    [ReferenceType],
                    [ReferenceId],
                    [ReferenceNumber],
                    [GoodsReceiptItemId],
                    [PerformedByUserId],
                    [PerformedByName],
                    [OccurredDate],
                    [OccurredAtUtc]
                )
                SELECT
                    source.[ProductId],
                    N'GoodsReceipt',
                    source.[QuantityReceived],
                    COALESCE(
                        SUM(source.[QuantityReceived]) OVER
                        (
                            PARTITION BY source.[ProductId]
                            ORDER BY source.[OccurredAtUtc], source.[GoodsReceiptItemId]
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                        ),
                        0
                    ),
                    SUM(source.[QuantityReceived]) OVER
                    (
                        PARTITION BY source.[ProductId]
                        ORDER BY source.[OccurredAtUtc], source.[GoodsReceiptItemId]
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                    ),
                    source.[ProductCode],
                    source.[ProductName],
                    source.[UnitOfMeasureCode],
                    N'GoodsReceipt',
                    source.[GoodsReceiptId],
                    source.[GoodsReceiptNumber],
                    source.[GoodsReceiptItemId],
                    source.[PerformedByUserId],
                    source.[PerformedByName],
                    CAST(source.[OccurredAtUtc] AS date),
                    source.[OccurredAtUtc]
                FROM
                (
                    SELECT
                        item.[Id] AS [GoodsReceiptItemId],
                        item.[GoodsReceiptId],
                        item.[ProductId],
                        item.[ProductCode],
                        item.[ProductName],
                        item.[UnitOfMeasureCode],
                        item.[QuantityReceived],
                        receipt.[GoodsReceiptNumber],
                        COALESCE(receipt.[PostedByUserId], receipt.[CreatedByUserId]) AS [PerformedByUserId],
                        COALESCE(receipt.[PostedByName], receipt.[CreatedByName]) AS [PerformedByName],
                        COALESCE(receipt.[PostedAtUtc], receipt.[CreatedAtUtc]) AS [OccurredAtUtc]
                    FROM [GoodsReceiptItems] AS item
                    INNER JOIN [GoodsReceipts] AS receipt
                        ON item.[GoodsReceiptId] = receipt.[Id]
                    WHERE receipt.[Status] = N'Posted'
                ) AS source;

                INSERT INTO [InventoryBalances]
                (
                    [ProductId],
                    [QuantityOnHand],
                    [LastUpdatedAtUtc]
                )
                SELECT
                    [ProductId],
                    SUM([QuantityChange]),
                    MAX([OccurredAtUtc])
                FROM [InventoryTransactions]
                GROUP BY [ProductId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");
        }
    }
}
