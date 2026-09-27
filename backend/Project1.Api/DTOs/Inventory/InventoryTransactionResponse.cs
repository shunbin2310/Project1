using Project1.Api.Entities;

namespace Project1.Api.DTOs.Inventory;

public sealed record InventoryTransactionResponse(
    int Id,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitOfMeasureCode,
    InventoryTransactionType Type,
    decimal QuantityChange,
    decimal QuantityBefore,
    decimal QuantityAfter,
    string ReferenceType,
    int ReferenceId,
    string ReferenceNumber,
    int? GoodsReceiptItemId,
    int PerformedByUserId,
    string PerformedByName,
    DateTimeOffset OccurredAtUtc);
