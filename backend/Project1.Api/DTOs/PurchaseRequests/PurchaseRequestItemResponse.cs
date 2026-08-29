namespace Project1.Api.DTOs.PurchaseRequests;

public sealed record PurchaseRequestItemResponse(
    int Id,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitOfMeasureCode,
    decimal Quantity,
    decimal EstimatedUnitPrice,
    decimal LineTotal);
