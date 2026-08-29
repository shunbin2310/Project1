using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.UnitsOfMeasure;
using Project1.Api.Entities;

namespace Project1.Api.Services.UnitsOfMeasure;

public sealed class UnitOfMeasureService(AppDbContext dbContext) : IUnitOfMeasureService
{
    public async Task<IReadOnlyList<UnitOfMeasureResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.UnitsOfMeasure.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(unit => unit.IsActive);
        }

        return await query
            .OrderBy(unit => unit.Code)
            .Select(unit => new UnitOfMeasureResponse(
                unit.Id,
                unit.Code,
                unit.Name,
                unit.Description,
                unit.IsActive,
                unit.CreatedAtUtc,
                unit.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<UnitOfMeasureResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.UnitsOfMeasure
            .AsNoTracking()
            .Where(unit => unit.Id == id)
            .Select(unit => new UnitOfMeasureResponse(
                unit.Id,
                unit.Code,
                unit.Name,
                unit.Description,
                unit.IsActive,
                unit.CreatedAtUtc,
                unit.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<UnitOfMeasureSaveResult> CreateAsync(
        CreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await CodeExistsAsync(code, cancellationToken))
        {
            return new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.DuplicateCode);
        }

        var unit = new UnitOfMeasure
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeOptionalText(request.Description)
        };

        dbContext.UnitsOfMeasure.Add(unit);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.Success, ToResponse(unit));
    }

    public async Task<UnitOfMeasureSaveResult> UpdateAsync(
        int id,
        UpdateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await dbContext.UnitsOfMeasure
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (unit is null)
        {
            return new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.NotFound);
        }

        unit.Name = request.Name.Trim();
        unit.Description = NormalizeOptionalText(request.Description);
        unit.IsActive = request.IsActive;
        unit.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.Success, ToResponse(unit));
    }

    public async Task<UnitOfMeasureSaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var unit = await dbContext.UnitsOfMeasure
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (unit is null)
        {
            return new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.NotFound);
        }

        if (unit.IsActive)
        {
            unit.IsActive = false;
            unit.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.Success, ToResponse(unit));
    }

    private Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        return dbContext.UnitsOfMeasure.AnyAsync(
            unit => unit.Code == code,
            cancellationToken);
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UnitOfMeasureResponse ToResponse(UnitOfMeasure unit) =>
        new(
            unit.Id,
            unit.Code,
            unit.Name,
            unit.Description,
            unit.IsActive,
            unit.CreatedAtUtc,
            unit.UpdatedAtUtc);
}
