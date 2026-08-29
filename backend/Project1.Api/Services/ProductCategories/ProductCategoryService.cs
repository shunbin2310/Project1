using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.ProductCategories;
using Project1.Api.Entities;

namespace Project1.Api.Services.ProductCategories;

public sealed class ProductCategoryService(AppDbContext dbContext) : IProductCategoryService
{
    public async Task<IReadOnlyList<ProductCategoryResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProductCategories.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query
            .OrderBy(category => category.Code)
            .Select(category => new ProductCategoryResponse(
                category.Id,
                category.Code,
                category.Name,
                category.Description,
                category.IsActive,
                category.CreatedAtUtc,
                category.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductCategoryResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProductCategories
            .AsNoTracking()
            .Where(category => category.Id == id)
            .Select(category => new ProductCategoryResponse(
                category.Id,
                category.Code,
                category.Name,
                category.Description,
                category.IsActive,
                category.CreatedAtUtc,
                category.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ProductCategorySaveResult> CreateAsync(
        CreateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (await NameExistsAsync(name, null, cancellationToken))
        {
            return new ProductCategorySaveResult(ProductCategorySaveStatus.DuplicateName);
        }

        var category = new ProductCategory
        {
            Code = CreateTemporaryCode(),
            Name = name,
            Description = NormalizeOptionalText(request.Description)
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        category.Code = $"CAT-{category.Id:D4}";
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProductCategorySaveResult(
            ProductCategorySaveStatus.Success,
            ToResponse(category));
    }

    public async Task<ProductCategorySaveResult> UpdateAsync(
        int id,
        UpdateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategories
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (category is null)
        {
            return new ProductCategorySaveResult(ProductCategorySaveStatus.NotFound);
        }

        var name = request.Name.Trim();

        if (await NameExistsAsync(name, id, cancellationToken))
        {
            return new ProductCategorySaveResult(ProductCategorySaveStatus.DuplicateName);
        }

        category.Name = name;
        category.Description = NormalizeOptionalText(request.Description);
        category.IsActive = request.IsActive;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProductCategorySaveResult(
            ProductCategorySaveStatus.Success,
            ToResponse(category));
    }

    public async Task<ProductCategorySaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategories
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (category is null)
        {
            return new ProductCategorySaveResult(ProductCategorySaveStatus.NotFound);
        }

        if (category.IsActive)
        {
            category.IsActive = false;
            category.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ProductCategorySaveResult(
            ProductCategorySaveStatus.Success,
            ToResponse(category));
    }

    private Task<bool> NameExistsAsync(
        string name,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        return dbContext.ProductCategories.AnyAsync(
            category => category.Name == name
                && (!excludedId.HasValue || category.Id != excludedId.Value),
            cancellationToken);
    }

    private static string CreateTemporaryCode() => $"TMP-{Guid.NewGuid():N}"[..20];

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProductCategoryResponse ToResponse(ProductCategory category) =>
        new(
            category.Id,
            category.Code,
            category.Name,
            category.Description,
            category.IsActive,
            category.CreatedAtUtc,
            category.UpdatedAtUtc);
}
