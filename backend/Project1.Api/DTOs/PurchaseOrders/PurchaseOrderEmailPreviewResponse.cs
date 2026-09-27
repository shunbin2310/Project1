using Project1.Api.Entities;

namespace Project1.Api.DTOs.PurchaseOrders;

public sealed record PurchaseOrderEmailPreviewResponse(
    int Id,
    string RecipientEmail,
    string Subject,
    string HtmlBody,
    EmailDeliveryStatus Status);
