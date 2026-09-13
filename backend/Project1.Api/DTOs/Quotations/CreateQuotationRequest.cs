using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Quotations;

public sealed class CreateQuotationRequest
{
    [Range(1, int.MaxValue)]
    public int PurchaseRequestId { get; init; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; init; }

    [StringLength(100)]
    public string? SupplierQuotationReference { get; init; }

    public DateOnly QuotationDate { get; init; }

    public DateOnly? ValidUntil { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    [MinLength(1)]
    public IReadOnlyList<QuotationItemPriceRequest> Items { get; init; } = [];
}
