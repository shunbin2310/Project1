using Project1.Api.DTOs.PurchaseRequests;

namespace Project1.Api.Services.PurchaseRequests;

public interface IPurchaseRequestService
{
    Task<IReadOnlyList<PurchaseRequestResponse>> GetAllAsync(
        string? stepCode,
        CancellationToken cancellationToken);

    Task<PurchaseRequestResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<PurchaseRequestOperationResult> CreateAsync(
        CreatePurchaseRequestRequest request,
        CancellationToken cancellationToken);

    Task<PurchaseRequestOperationResult> UpdateAsync(
        int id,
        UpdatePurchaseRequestRequest request,
        CancellationToken cancellationToken);

    Task<PurchaseRequestOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<PurchaseRequestOperationResult> ExecuteActionAsync(
        int id,
        string actionCode,
        PurchaseRequestActionRequest request,
        CancellationToken cancellationToken);
}
