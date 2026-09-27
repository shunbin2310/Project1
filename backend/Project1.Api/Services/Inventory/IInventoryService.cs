using Project1.Api.DTOs.Inventory;
using Project1.Api.Entities;

namespace Project1.Api.Services.Inventory;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryBalanceResponse>> GetBalancesAsync(
        int? productCategoryId,
        bool? lowStock,
        bool includeInactive,
        string? search,
        CancellationToken cancellationToken);

    Task<InventoryBalanceResponse?> GetBalanceAsync(
        int productId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryTransactionResponse>> GetTransactionsAsync(
        int productId,
        InventoryTransactionType? type,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken);
}
