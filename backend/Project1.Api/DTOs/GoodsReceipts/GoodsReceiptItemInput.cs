using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.GoodsReceipts;

public sealed class GoodsReceiptItemInput
{
    [Range(1, int.MaxValue)]
    public int PurchaseOrderItemId { get; init; }

    [Range(typeof(decimal), "0.001", "999999999999999.999")]
    public decimal QuantityReceived { get; init; }
}
