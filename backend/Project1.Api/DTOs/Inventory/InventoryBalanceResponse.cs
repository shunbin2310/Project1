namespace Project1.Api.DTOs.Inventory;

public sealed record InventoryBalanceResponse(
    int ProductId,
    string ProductCode,
    string ProductName,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    bool IsLowStock,
    bool IsProductActive,
    DateTimeOffset? LastUpdatedAtUtc);
