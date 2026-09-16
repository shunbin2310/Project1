using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.GoodsReceipts;

public sealed class UpdateGoodsReceiptRequest
{
    [StringLength(100)]
    public string? SupplierDeliveryNoteNumber { get; init; }

    public DateOnly ReceivedDate { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    [MinLength(1)]
    public IReadOnlyList<GoodsReceiptItemInput> Items { get; init; } = [];
}
