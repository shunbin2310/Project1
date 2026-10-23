using Microsoft.AspNetCore.Authorization;
using Project1.Api.Authentication;
using Project1.Api.Controllers;

namespace Project1.Api.Tests.Controllers;

public sealed class RoleAuthorizationTests
{
    [Fact]
    public void ProcurementControllers_AllowProcurementAndAdmin()
    {
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ControllerRoles<QuotationsController>());
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ActionRoles<SuppliersController>(nameof(SuppliersController.Create)));
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ActionRoles<SupplierProductsController>(nameof(SupplierProductsController.Create)));
    }

    [Fact]
    public void ProductMutations_AllowProcurementOfficerAndAdmin()
    {
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ActionRoles<ProductsController>(nameof(ProductsController.Create)));
        Assert.Equal(ApplicationRoles.AdminOrProcurement,
            ActionRoles<ProductsController>(nameof(ProductsController.Update)));
        Assert.Equal(ApplicationRoles.AdminOrProcurement,
            ActionRoles<ProductsController>(nameof(ProductsController.Deactivate)));
    }

    [Fact]
    public void PurchaseOrders_AllowOperationalReadersButOnlyProcurementCanMutate()
    {
        Assert.Equal(
            ApplicationRoles.PurchaseOrderReaders,
            ControllerRoles<PurchaseOrdersController>());
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ActionRoles<PurchaseOrdersController>(nameof(PurchaseOrdersController.Create)));
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ActionRoles<PurchaseOrdersController>(nameof(PurchaseOrdersController.Issue)));
    }

    [Fact]
    public void EmailRecords_AllowProcurementAndAdmin()
    {
        Assert.Equal(
            ApplicationRoles.AdminOrProcurement,
            ControllerRoles<EmailRecordsController>());
    }

    [Fact]
    public void GoodsReceipts_AllowOperationalReadersButOnlyWarehouseCanMutate()
    {
        Assert.Equal(
            ApplicationRoles.GoodsReceiptReaders,
            ControllerRoles<GoodsReceiptsController>());
        Assert.Equal(
            ApplicationRoles.AdminOrWarehouse,
            ActionRoles<GoodsReceiptsController>(nameof(GoodsReceiptsController.Create)));
        Assert.Equal(
            ApplicationRoles.AdminOrWarehouse,
            ActionRoles<GoodsReceiptsController>(nameof(GoodsReceiptsController.Post)));
    }

    [Fact]
    public void Inventory_AllowsOperationalReaders()
    {
        Assert.Equal(
            ApplicationRoles.InventoryReaders,
            ControllerRoles<InventoryController>());
    }

    private static string? ControllerRoles<TController>() =>
        Assert.Single(typeof(TController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()).Roles;

    private static string? ActionRoles<TController>(string actionName) =>
        Assert.Single(typeof(TController)
            .GetMethod(actionName)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()).Roles;
}
