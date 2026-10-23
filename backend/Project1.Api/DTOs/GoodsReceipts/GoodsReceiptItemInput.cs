using System.ComponentModel.DataAnnotations;
using Project1.Api.Validation;

namespace Project1.Api.DTOs.GoodsReceipts;

public sealed class GoodsReceiptItemInput
{
    [Range(1, int.MaxValue)]
    public int PurchaseOrderItemId { get; init; }

    [Range(typeof(decimal), "1", "999999999999999")]
    [WholeNumber]
    public decimal QuantityReceived { get; init; }
}
