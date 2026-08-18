using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Departments;
using Project1.Api.Entities;

namespace Project1.Api.Services.Departments;

public sealed class DepartmentService(AppDbContext dbContext) : IDepartmentService
{
    public async Task<IReadOnlyList<DepartmentResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Departments.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        return await query
            .OrderBy(department => department.Code)
            .Select(department => new DepartmentResponse(
                department.Id,
                department.Code,
                department.Name,
                department.Description,
                department.IsActive,
                department.CreatedAtUtc,
                department.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<DepartmentResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Departments
            .AsNoTracking()
            .Where(department => department.Id == id)
            .Select(department => new DepartmentResponse(
                department.Id,
                department.Code,
                department.Name,
                department.Description,
                department.IsActive,
                department.CreatedAtUtc,
                department.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<DepartmentSaveResult> CreateAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(request.Code);

        if (await CodeExistsAsync(normalizedCode, null, cancellationToken))
        {
            return new DepartmentSaveResult(DepartmentSaveStatus.DuplicateCode);
        }

        var department = new Department
        {
            Code = normalizedCode,
            Name = request.Name.Trim(),
            Description = NormalizeDescription(request.Description)
        };

        dbContext.Departments.Add(department);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DepartmentSaveResult(
            DepartmentSaveStatus.Success,
            ToResponse(department));
    }

    public async Task<DepartmentSaveResult> UpdateAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await dbContext.Departments
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (department is null)
        {
            return new DepartmentSaveResult(DepartmentSaveStatus.NotFound);
        }

        var normalizedCode = NormalizeCode(request.Code);

        if (await CodeExistsAsync(normalizedCode, id, cancellationToken))
        {
            return new DepartmentSaveResult(DepartmentSaveStatus.DuplicateCode);
        }

        department.Code = normalizedCode;
        department.Name = request.Name.Trim();
        department.Description = NormalizeDescription(request.Description);
        department.IsActive = request.IsActive;
        department.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DepartmentSaveResult(
            DepartmentSaveStatus.Success,
            ToResponse(department));
    }

    public async Task<DepartmentSaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var department = await dbContext.Departments
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (department is null)
        {
            return new DepartmentSaveResult(DepartmentSaveStatus.NotFound);
        }

        if (department.IsActive)
        {
            department.IsActive = false;
            department.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new DepartmentSaveResult(
            DepartmentSaveStatus.Success,
            ToResponse(department));
    }

    private Task<bool> CodeExistsAsync(
        string code,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        return dbContext.Departments.AnyAsync(
            department => department.Code == code
                && (!excludedId.HasValue || department.Id != excludedId.Value),
            cancellationToken);
    }

    private static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static DepartmentResponse ToResponse(Department department) =>
        new(
            department.Id,
            department.Code,
            department.Name,
            department.Description,
            department.IsActive,
            department.CreatedAtUtc,
            department.UpdatedAtUtc);
}
