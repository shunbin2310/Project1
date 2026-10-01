namespace Project1.Api.Services.PurchaseOrders;

public static class PurchaseOrderWorkflow
{
    public const string TemplateCode = "PURCHASE_ORDER";
    public const string TemplateName = "Purchase Order Approval";
    public const string EntityType = "PurchaseOrder";

    public const string DraftStep = "DRAFT";
    public const string PendingApprovalStep = "PENDING_APPROVAL";
    public const string ApprovedStep = "APPROVED";

    public const string SubmitAction = "SUBMIT";
    public const string ApproveAction = "APPROVE";
    public const string RejectAction = "REJECT";
}
