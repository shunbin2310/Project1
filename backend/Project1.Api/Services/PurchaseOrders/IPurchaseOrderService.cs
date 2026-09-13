using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.Entities;

namespace Project1.Api.Services.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<IReadOnlyList<PurchaseOrderResponse>> GetAllAsync(
        int? supplierId,
        PurchaseOrderStatus? status,
        CancellationToken cancellationToken);

    Task<PurchaseOrderResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<PurchaseOrderOperationResult> CreateAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken);

    Task<PurchaseOrderOperationResult> UpdateAsync(
        int id,
        UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken);

    Task<PurchaseOrderOperationResult> IssueAsync(int id, CancellationToken cancellationToken);

    Task<PurchaseOrderOperationResult> CancelAsync(
        int id,
        CancelPurchaseOrderRequest request,
        CancellationToken cancellationToken);

    Task<PurchaseOrderOperationResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
