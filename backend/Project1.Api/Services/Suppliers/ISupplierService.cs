using Project1.Api.DTOs.Suppliers;

namespace Project1.Api.Services.Suppliers;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<SupplierResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<SupplierSaveResult> CreateAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken);

    Task<SupplierSaveResult> UpdateAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken);

    Task<SupplierSaveResult> DeactivateAsync(
        int id,
        CancellationToken cancellationToken);
}
