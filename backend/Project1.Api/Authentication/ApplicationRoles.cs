namespace Project1.Api.Authentication;

public static class ApplicationRoles
{
    public const string Requester = "REQUESTER";
    public const string DepartmentApprover = "DEPARTMENT_APPROVER";
    public const string FinanceApprover = "FINANCE_APPROVER";
    public const string ProcurementOfficer = "PROCUREMENT_OFFICER";
    public const string PurchaseOrderApprover = "PURCHASE_ORDER_APPROVER";
    public const string WarehouseOfficer = "WAREHOUSE_OFFICER";
    public const string CatalogManager = "CATALOG_MANAGER";
    public const string Admin = "ADMIN";

    public const string AdminOrProcurement = Admin + "," + ProcurementOfficer;
    public const string AdminOrWarehouse = Admin + "," + WarehouseOfficer;
    public const string AdminOrCatalog = Admin + "," + CatalogManager;
    public const string PurchaseRequestReaders =
        Admin + "," + Requester + "," + DepartmentApprover + "," + FinanceApprover + "," +
        ProcurementOfficer + "," + WarehouseOfficer + "," + CatalogManager;
    public const string PurchaseOrderReaders =
        Admin + "," + ProcurementOfficer + "," + PurchaseOrderApprover + "," + WarehouseOfficer;
    public const string GoodsReceiptReaders =
        Admin + "," + ProcurementOfficer + "," + WarehouseOfficer;
    public const string InventoryReaders =
        Admin + "," + ProcurementOfficer + "," + WarehouseOfficer;

    public static readonly string[] All =
    [
        Requester,
        DepartmentApprover,
        FinanceApprover,
        ProcurementOfficer,
        PurchaseOrderApprover,
        WarehouseOfficer,
        CatalogManager,
        Admin
    ];
}
