using Project1.Api.DTOs.Departments;

namespace Project1.Api.Services.Departments;

public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<DepartmentResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<DepartmentSaveResult> CreateAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken);

    Task<DepartmentSaveResult> UpdateAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken);

    Task<DepartmentSaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken);
}
