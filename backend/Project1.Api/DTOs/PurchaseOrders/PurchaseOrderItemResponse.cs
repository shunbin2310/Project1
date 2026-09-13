namespace Project1.Api.DTOs.PurchaseOrders;

public sealed record PurchaseOrderItemResponse(
    int Id,
    int QuotationItemId,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitOfMeasureCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);
