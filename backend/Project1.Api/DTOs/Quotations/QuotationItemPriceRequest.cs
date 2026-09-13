using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.Quotations;

public sealed class QuotationItemPriceRequest
{
    [Range(1, int.MaxValue)]
    public int PurchaseRequestItemId { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal UnitPrice { get; init; }
}
