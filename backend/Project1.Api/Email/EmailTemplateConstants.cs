namespace Project1.Api.Email;

public static class EmailTemplateConstants
{
    public const string PurchaseOrderIssuedCode = "PURCHASE_ORDER_ISSUED";

    public const string PurchaseOrderIssuedName = "Purchase Order Issued Email";

    public const string SupplierEmailRule = "SUPPLIER_EMAIL";

    public static readonly IReadOnlyList<string> PurchaseOrderPlaceholders =
    [
        "PurchaseOrderNumber",
        "SupplierName",
        "SupplierEmail",
        "OrderDate",
        "ExpectedDeliveryDate",
        "DeliveryAddress",
        "SupplierQuotationReference",
        "ItemsTable",
        "TotalAmount",
        "Notes"
    ];
}
