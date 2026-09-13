using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Quotations;

public sealed class UpdateQuotationRequest
{
    [StringLength(100)]
    public string? SupplierQuotationReference { get; init; }

    public DateOnly QuotationDate { get; init; }

    public DateOnly? ValidUntil { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    [MinLength(1)]
    public IReadOnlyList<QuotationItemPriceRequest> Items { get; init; } = [];
}
