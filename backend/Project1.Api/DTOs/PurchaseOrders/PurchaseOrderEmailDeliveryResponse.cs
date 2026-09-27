using Project1.Api.Entities;

namespace Project1.Api.DTOs.PurchaseOrders;

public sealed record PurchaseOrderEmailDeliveryResponse(
    int Id,
    string RecipientEmail,
    string Subject,
    EmailDeliveryStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? SentAtUtc,
    string? LastError);
