namespace Project1.Api.DTOs.GoodsReceipts;

public sealed record GoodsReceiptItemResponse(
    int Id,
    int PurchaseOrderItemId,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitOfMeasureCode,
    decimal OrderedQuantity,
    decimal QuantityReceived);
