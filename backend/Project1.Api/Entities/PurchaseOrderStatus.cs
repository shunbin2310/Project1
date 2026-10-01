namespace Project1.Api.Entities;

public enum PurchaseOrderStatus
{
    Draft,
    PendingApproval,
    Approved,
    Issued,
    PartiallyReceived,
    Received,
    Cancelled
}
