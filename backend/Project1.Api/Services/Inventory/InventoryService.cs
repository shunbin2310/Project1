using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Inventory;
using Project1.Api.Entities;

namespace Project1.Api.Services.Inventory;

public sealed class InventoryService(AppDbContext dbContext) : IInventoryService
{
    public async Task<IReadOnlyList<InventoryBalanceResponse>> GetBalancesAsync(
        int? productCategoryId,
        bool? lowStock,
        bool includeInactive,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Products
            .AsNoTracking()
            .Include(product => product.ProductCategory)
            .Include(product => product.UnitOfMeasure)
            .Include(product => product.InventoryBalance)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(product => product.IsActive);
        }

        if (productCategoryId.HasValue)
        {
            query = query.Where(product => product.ProductCategoryId == productCategoryId.Value);
        }

        if (lowStock.HasValue)
        {
            query = lowStock.Value
                ? query.Where(product =>
                    (product.InventoryBalance == null
                        ? 0m
                        : product.InventoryBalance.QuantityOnHand) <= product.ReorderLevel)
                : query.Where(product =>
                    (product.InventoryBalance == null
                        ? 0m
                        : product.InventoryBalance.QuantityOnHand) > product.ReorderLevel);
        }

        var normalizedSearch = search?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(product =>
                product.Code.ToUpper().Contains(normalizedSearch) ||
                product.Name.ToUpper().Contains(normalizedSearch) ||
                product.ProductCategory.Code.ToUpper().Contains(normalizedSearch) ||
                product.ProductCategory.Name.ToUpper().Contains(normalizedSearch));
        }

        var products = await query
            .OrderBy(product => product.Code)
            .ToListAsync(cancellationToken);

        return products.Select(ToBalanceResponse).ToList();
    }

    public async Task<InventoryBalanceResponse?> GetBalanceAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Include(item => item.ProductCategory)
            .Include(item => item.UnitOfMeasure)
            .Include(item => item.InventoryBalance)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

        return product is null ? null : ToBalanceResponse(product);
    }

    public async Task<IReadOnlyList<InventoryTransactionResponse>> GetTransactionsAsync(
        int productId,
        InventoryTransactionType? type,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var query = dbContext.InventoryTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ProductId == productId);

        if (type.HasValue)
        {
            query = query.Where(transaction => transaction.Type == type.Value);
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(transaction => transaction.OccurredDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(transaction => transaction.OccurredDate <= dateTo.Value);
        }

        return await query
            .OrderByDescending(transaction => transaction.OccurredDate)
            .ThenByDescending(transaction => transaction.Id)
            .Select(transaction => new InventoryTransactionResponse(
                transaction.Id,
                transaction.ProductId,
                transaction.ProductCode,
                transaction.ProductName,
                transaction.UnitOfMeasureCode,
                transaction.Type,
                transaction.QuantityChange,
                transaction.QuantityBefore,
                transaction.QuantityAfter,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.GoodsReceiptItemId,
                transaction.PerformedByUserId,
                transaction.PerformedByName,
                transaction.OccurredAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static InventoryBalanceResponse ToBalanceResponse(Product product)
    {
        var quantityOnHand = product.InventoryBalance?.QuantityOnHand ?? 0m;

        return new InventoryBalanceResponse(
            product.Id,
            product.Code,
            product.Name,
            product.ProductCategoryId,
            product.ProductCategory.Code,
            product.ProductCategory.Name,
            product.UnitOfMeasure.Code,
            product.UnitOfMeasure.Name,
            quantityOnHand,
            product.ReorderLevel,
            quantityOnHand <= product.ReorderLevel,
            product.IsActive,
            product.InventoryBalance?.LastUpdatedAtUtc);
    }
}
