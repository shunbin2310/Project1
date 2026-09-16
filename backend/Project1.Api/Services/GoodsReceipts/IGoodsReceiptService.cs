using Project1.Api.DTOs.GoodsReceipts;
using Project1.Api.Entities;

namespace Project1.Api.Services.GoodsReceipts;

public interface IGoodsReceiptService
{
    Task<IReadOnlyList<GoodsReceiptResponse>> GetAllAsync(
        int? purchaseOrderId,
        int? supplierId,
        GoodsReceiptStatus? status,
        CancellationToken cancellationToken);

    Task<GoodsReceiptResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<GoodsReceiptOperationResult> CreateAsync(
        CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken);

    Task<GoodsReceiptOperationResult> UpdateAsync(
        int id,
        UpdateGoodsReceiptRequest request,
        CancellationToken cancellationToken);

    Task<GoodsReceiptOperationResult> PostAsync(int id, CancellationToken cancellationToken);

    Task<GoodsReceiptOperationResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
