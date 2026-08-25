using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Suppliers;
using Project1.Api.Entities;

namespace Project1.Api.Services.Suppliers;

public sealed class SupplierService(AppDbContext dbContext) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Suppliers.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(supplier => supplier.IsActive);
        }

        return await query
            .OrderBy(supplier => supplier.Code)
            .Select(supplier => new SupplierResponse(
                supplier.Id,
                supplier.Code,
                supplier.Name,
                supplier.ContactPerson,
                supplier.Email,
                supplier.Phone,
                supplier.Address,
                supplier.IsActive,
                supplier.CreatedAtUtc,
                supplier.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.Id == id)
            .Select(supplier => new SupplierResponse(
                supplier.Id,
                supplier.Code,
                supplier.Name,
                supplier.ContactPerson,
                supplier.Email,
                supplier.Phone,
                supplier.Address,
                supplier.IsActive,
                supplier.CreatedAtUtc,
                supplier.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SupplierSaveResult> CreateAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var supplier = new Supplier
        {
            Code = CreateTemporaryCode(),
            Name = request.Name.Trim(),
            ContactPerson = NormalizeOptionalText(request.ContactPerson),
            Email = NormalizeOptionalText(request.Email),
            Phone = NormalizeOptionalText(request.Phone),
            Address = NormalizeOptionalText(request.Address)
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);

        supplier.Code = FormatSupplierCode(supplier.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SupplierSaveResult(
            SupplierSaveStatus.Success,
            ToResponse(supplier));
    }

    public async Task<SupplierSaveResult> UpdateAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (supplier is null)
        {
            return new SupplierSaveResult(SupplierSaveStatus.NotFound);
        }

        supplier.Name = request.Name.Trim();
        supplier.ContactPerson = NormalizeOptionalText(request.ContactPerson);
        supplier.Email = NormalizeOptionalText(request.Email);
        supplier.Phone = NormalizeOptionalText(request.Phone);
        supplier.Address = NormalizeOptionalText(request.Address);
        supplier.IsActive = request.IsActive;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SupplierSaveResult(
            SupplierSaveStatus.Success,
            ToResponse(supplier));
    }

    public async Task<SupplierSaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (supplier is null)
        {
            return new SupplierSaveResult(SupplierSaveStatus.NotFound);
        }

        if (supplier.IsActive)
        {
            supplier.IsActive = false;
            supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SupplierSaveResult(
            SupplierSaveStatus.Success,
            ToResponse(supplier));
    }

    private static string CreateTemporaryCode() =>
        $"TMP-{Guid.NewGuid():N}"[..20];

    private static string FormatSupplierCode(int id) =>
        $"SUP-{id:D4}";

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SupplierResponse ToResponse(Supplier supplier) =>
        new(
            supplier.Id,
            supplier.Code,
            supplier.Name,
            supplier.ContactPerson,
            supplier.Email,
            supplier.Phone,
            supplier.Address,
            supplier.IsActive,
            supplier.CreatedAtUtc,
            supplier.UpdatedAtUtc);
}
