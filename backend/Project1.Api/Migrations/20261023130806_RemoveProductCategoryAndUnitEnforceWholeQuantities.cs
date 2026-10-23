using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProductCategoryAndUnitEnforceWholeQuantities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Check every quantity before dropping catalog data. Never round existing records.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [dbo].[Products] WHERE [ReorderLevel] <> ROUND([ReorderLevel], 0))
                    THROW 51000, 'Non-integer quantities in Products. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[PurchaseRequestItems] WHERE [Quantity] <> ROUND([Quantity], 0))
                    THROW 51000, 'Non-integer quantities in PurchaseRequestItems. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[QuotationItems] WHERE [Quantity] <> ROUND([Quantity], 0))
                    THROW 51000, 'Non-integer quantities in QuotationItems. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[PurchaseOrderItems] WHERE [Quantity] <> ROUND([Quantity], 0))
                    THROW 51000, 'Non-integer quantities in PurchaseOrderItems. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[GoodsReceiptItems] WHERE [OrderedQuantity] <> ROUND([OrderedQuantity], 0) OR [QuantityReceived] <> ROUND([QuantityReceived], 0))
                    THROW 51000, 'Non-integer quantities in GoodsReceiptItems. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[InventoryBalances] WHERE [QuantityOnHand] <> ROUND([QuantityOnHand], 0))
                    THROW 51000, 'Non-integer quantities in InventoryBalances. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                IF EXISTS (SELECT 1 FROM [dbo].[InventoryTransactions] WHERE [QuantityChange] <> ROUND([QuantityChange], 0) OR [QuantityBefore] <> ROUND([QuantityBefore], 0) OR [QuantityAfter] <> ROUND([QuantityAfter], 0))
                    THROW 51000, 'Non-integer quantities in InventoryTransactions. Review and correct these records before retrying this migration; no quantities were rounded.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductCategories_ProductCategoryId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_UnitsOfMeasure_UnitOfMeasureId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure");

            migrationBuilder.DropIndex(
                name: "IX_Products_ProductCategoryId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_UnitOfMeasureId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureCode",
                table: "QuotationItems");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureCode",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "ProductCategoryId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureCode",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureCode",
                table: "GoodsReceiptItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuotationItems_WholeQuantities",
                table: "QuotationItems",
                sql: "[Quantity] = ROUND([Quantity], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequestItems_WholeQuantities",
                table: "PurchaseRequestItems",
                sql: "[Quantity] = ROUND([Quantity], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderItems_WholeQuantities",
                table: "PurchaseOrderItems",
                sql: "[Quantity] = ROUND([Quantity], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_WholeQuantities",
                table: "Products",
                sql: "[ReorderLevel] = ROUND([ReorderLevel], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_WholeQuantities",
                table: "InventoryTransactions",
                sql: "[QuantityChange] = ROUND([QuantityChange], 0) AND [QuantityBefore] = ROUND([QuantityBefore], 0) AND [QuantityAfter] = ROUND([QuantityAfter], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryBalances_WholeQuantities",
                table: "InventoryBalances",
                sql: "[QuantityOnHand] = ROUND([QuantityOnHand], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GoodsReceiptItems_WholeQuantities",
                table: "GoodsReceiptItems",
                sql: "[OrderedQuantity] = ROUND([OrderedQuantity], 0) AND [QuantityReceived] = ROUND([QuantityReceived], 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Category and unit data cannot be reconstructed. Restore the verified pre-migration database backup to roll back this change.");
        }
    }
}
