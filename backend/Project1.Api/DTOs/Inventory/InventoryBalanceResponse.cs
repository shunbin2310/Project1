namespace Project1.Api.DTOs.Inventory;

public sealed record InventoryBalanceResponse(
    int ProductId,
    string ProductCode,
    string ProductName,
    int ProductCategoryId,
    string ProductCategoryCode,
    string ProductCategoryName,
    string UnitOfMeasureCode,
    string UnitOfMeasureName,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    bool IsLowStock,
    bool IsProductActive,
    DateTimeOffset? LastUpdatedAtUtc);
