namespace Project1.Api.DTOs.Quotations;

public sealed record QuotationItemResponse(
    int Id,
    int PurchaseRequestItemId,
    int ProductId,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);
