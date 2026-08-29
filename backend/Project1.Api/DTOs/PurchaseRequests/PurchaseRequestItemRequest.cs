using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed class PurchaseRequestItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(typeof(decimal), "0", "999999999999999.999")]
    public decimal Quantity { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal? EstimatedUnitPrice { get; init; }
}
