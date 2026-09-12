using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.SupplierProducts;
using Project1.Api.Entities;

namespace Project1.Api.Services.SupplierProducts;

public sealed class SupplierProductService(AppDbContext dbContext) : ISupplierProductService
{
    public async Task<IReadOnlyList<SupplierProductResponse>> GetAllAsync(
        int? supplierId,
        int? productId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SupplierProducts.AsNoTracking();

        if (supplierId.HasValue)
        {
            query = query.Where(item => item.SupplierId == supplierId.Value);
        }

        if (productId.HasValue)
        {
            query = query.Where(item => item.ProductId == productId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(item =>
                item.IsActive && item.Supplier.IsActive && item.Product.IsActive);
        }

        return await ProjectResponses(
                query
                    .OrderBy(item => item.Product.Code)
                    .ThenBy(item => item.Supplier.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierProductResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await ProjectResponses(
                dbContext.SupplierProducts
                    .AsNoTracking()
                    .Where(item => item.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SupplierProductSaveResult> CreateAsync(
        CreateSupplierProductRequest request,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(
            item => item.Id == request.SupplierId && item.IsActive,
            cancellationToken);

        if (supplier is null)
        {
            return new SupplierProductSaveResult(
                SupplierProductSaveStatus.SupplierUnavailable);
        }

        var product = await dbContext.Products
            .Include(item => item.UnitOfMeasure)
            .SingleOrDefaultAsync(
                item => item.Id == request.ProductId && item.IsActive,
                cancellationToken);

        if (product is null)
        {
            return new SupplierProductSaveResult(
                SupplierProductSaveStatus.ProductUnavailable);
        }

        var exists = await dbContext.SupplierProducts.AnyAsync(
            item => item.SupplierId == request.SupplierId &&
                    item.ProductId == request.ProductId,
            cancellationToken);

        if (exists)
        {
            return new SupplierProductSaveResult(
                SupplierProductSaveStatus.DuplicateRelationship);
        }

        var supplierProduct = new SupplierProduct
        {
            Supplier = supplier,
            Product = product,
            IsPreferred = request.IsPreferred
        };

        dbContext.SupplierProducts.Add(supplierProduct);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SupplierProductSaveResult(
            SupplierProductSaveStatus.Success,
            ToResponse(supplierProduct));
    }

    public async Task<SupplierProductSaveResult> UpdateAsync(
        int id,
        UpdateSupplierProductRequest request,
        CancellationToken cancellationToken)
    {
        var supplierProduct = await dbContext.SupplierProducts
            .Include(item => item.Supplier)
            .Include(item => item.Product)
                .ThenInclude(product => product.UnitOfMeasure)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (supplierProduct is null)
        {
            return new SupplierProductSaveResult(SupplierProductSaveStatus.NotFound);
        }

        if (request.IsActive && !supplierProduct.Supplier.IsActive)
        {
            return new SupplierProductSaveResult(
                SupplierProductSaveStatus.SupplierUnavailable);
        }

        if (request.IsActive && !supplierProduct.Product.IsActive)
        {
            return new SupplierProductSaveResult(
                SupplierProductSaveStatus.ProductUnavailable);
        }

        supplierProduct.IsPreferred = request.IsPreferred;
        supplierProduct.IsActive = request.IsActive;
        supplierProduct.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SupplierProductSaveResult(
            SupplierProductSaveStatus.Success,
            ToResponse(supplierProduct));
    }

    public async Task<SupplierProductSaveResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var supplierProduct = await dbContext.SupplierProducts.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

        if (supplierProduct is null)
        {
            return new SupplierProductSaveResult(SupplierProductSaveStatus.NotFound);
        }

        dbContext.SupplierProducts.Remove(supplierProduct);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SupplierProductSaveResult(SupplierProductSaveStatus.Success);
    }

    private static IQueryable<SupplierProductResponse> ProjectResponses(
        IQueryable<SupplierProduct> query)
    {
        return query.Select(item => new SupplierProductResponse(
            item.Id,
            item.SupplierId,
            item.Supplier.Code,
            item.Supplier.Name,
            item.ProductId,
            item.Product.Code,
            item.Product.Name,
            item.Product.UnitOfMeasure.Code,
            item.Product.UnitOfMeasure.Name,
            item.Product.DefaultUnitPrice,
            item.IsPreferred,
            item.IsActive,
            item.CreatedAtUtc,
            item.UpdatedAtUtc));
    }

    private static SupplierProductResponse ToResponse(SupplierProduct item) =>
        new(
            item.Id,
            item.SupplierId,
            item.Supplier.Code,
            item.Supplier.Name,
            item.ProductId,
            item.Product.Code,
            item.Product.Name,
            item.Product.UnitOfMeasure.Code,
            item.Product.UnitOfMeasure.Name,
            item.Product.DefaultUnitPrice,
            item.IsPreferred,
            item.IsActive,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);
}
