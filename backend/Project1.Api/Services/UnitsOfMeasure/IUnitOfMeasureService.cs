using Project1.Api.DTOs.UnitsOfMeasure;

namespace Project1.Api.Services.UnitsOfMeasure;

public interface IUnitOfMeasureService
{
    Task<IReadOnlyList<UnitOfMeasureResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<UnitOfMeasureResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<UnitOfMeasureSaveResult> CreateAsync(
        CreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken);

    Task<UnitOfMeasureSaveResult> UpdateAsync(
        int id,
        UpdateUnitOfMeasureRequest request,
        CancellationToken cancellationToken);

    Task<UnitOfMeasureSaveResult> DeactivateAsync(int id, CancellationToken cancellationToken);
}
