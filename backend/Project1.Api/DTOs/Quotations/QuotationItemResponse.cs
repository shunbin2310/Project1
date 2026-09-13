namespace Project1.Api.DTOs.Quotations;

public sealed record QuotationItemResponse(
    int Id,
    int PurchaseRequestItemId,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitOfMeasureCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);
