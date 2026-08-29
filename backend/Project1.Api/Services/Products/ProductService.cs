using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Products;
using Project1.Api.Entities;

namespace Project1.Api.Services.Products;

public sealed class ProductService(AppDbContext dbContext) : IProductService
{
    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Products.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(product => product.IsActive);
        }

        return await ProjectResponses(query.OrderBy(product => product.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await ProjectResponses(
                dbContext.Products.AsNoTracking().Where(product => product.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ProductSaveResult> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategories.SingleOrDefaultAsync(
            item => item.Id == request.ProductCategoryId && item.IsActive,
            cancellationToken);

        if (category is null)
        {
            return new ProductSaveResult(ProductSaveStatus.ProductCategoryUnavailable);
        }

        var unit = await dbContext.UnitsOfMeasure.SingleOrDefaultAsync(
            item => item.Id == request.UnitOfMeasureId && item.IsActive,
            cancellationToken);

        if (unit is null)
        {
            return new ProductSaveResult(ProductSaveStatus.UnitOfMeasureUnavailable);
        }

        var product = new Product
        {
            Code = CreateTemporaryCode(),
            Name = request.Name.Trim(),
            Description = NormalizeOptionalText(request.Description),
            ProductCategory = category,
            UnitOfMeasure = unit,
            DefaultUnitPrice = request.DefaultUnitPrice,
            ReorderLevel = request.ReorderLevel
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        product.Code = $"ITEM-{product.Id:D4}";
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProductSaveResult(ProductSaveStatus.Success, ToResponse(product));
    }

    public async Task<ProductSaveResult> UpdateAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .Include(item => item.ProductCategory)
            .Include(item => item.UnitOfMeasure)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (product is null)
        {
            return new ProductSaveResult(ProductSaveStatus.NotFound);
        }

        if (request.ProductCategoryId != product.ProductCategoryId)
        {
            var category = await dbContext.ProductCategories.SingleOrDefaultAsync(
                item => item.Id == request.ProductCategoryId && item.IsActive,
                cancellationToken);

            if (category is null)
            {
                return new ProductSaveResult(ProductSaveStatus.ProductCategoryUnavailable);
            }

            product.ProductCategory = category;
        }

        if (request.UnitOfMeasureId != product.UnitOfMeasureId)
        {
            var unit = await dbContext.UnitsOfMeasure.SingleOrDefaultAsync(
                item => item.Id == request.UnitOfMeasureId && item.IsActive,
                cancellationToken);

            if (unit is null)
            {
                return new ProductSaveResult(ProductSaveStatus.UnitOfMeasureUnavailable);
            }

            product.UnitOfMeasure = unit;
        }

        product.Name = request.Name.Trim();
        product.Description = NormalizeOptionalText(request.Description);
        product.DefaultUnitPrice = request.DefaultUnitPrice;
        product.ReorderLevel = request.ReorderLevel;
        product.IsActive = request.IsActive;
        product.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProductSaveResult(ProductSaveStatus.Success, ToResponse(product));
    }

    public async Task<ProductSaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .Include(item => item.ProductCategory)
            .Include(item => item.UnitOfMeasure)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (product is null)
        {
            return new ProductSaveResult(ProductSaveStatus.NotFound);
        }

        if (product.IsActive)
        {
            product.IsActive = false;
            product.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ProductSaveResult(ProductSaveStatus.Success, ToResponse(product));
    }

    private static IQueryable<ProductResponse> ProjectResponses(IQueryable<Product> query)
    {
        return query.Select(product => new ProductResponse(
            product.Id,
            product.Code,
            product.Name,
            product.Description,
            product.ProductCategoryId,
            product.ProductCategory.Code,
            product.ProductCategory.Name,
            product.UnitOfMeasureId,
            product.UnitOfMeasure.Code,
            product.UnitOfMeasure.Name,
            product.DefaultUnitPrice,
            product.ReorderLevel,
            product.IsActive,
            product.CreatedAtUtc,
            product.UpdatedAtUtc));
    }

    private static string CreateTemporaryCode() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProductResponse ToResponse(Product product) =>
        new(
            product.Id,
            product.Code,
            product.Name,
            product.Description,
            product.ProductCategoryId,
            product.ProductCategory.Code,
            product.ProductCategory.Name,
            product.UnitOfMeasureId,
            product.UnitOfMeasure.Code,
            product.UnitOfMeasure.Name,
            product.DefaultUnitPrice,
            product.ReorderLevel,
            product.IsActive,
            product.CreatedAtUtc,
            product.UpdatedAtUtc);
}
