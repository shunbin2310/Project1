using System.ComponentModel.DataAnnotations;
using Project1.Api.Validation;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed class PurchaseRequestItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(typeof(decimal), "0", "999999999999999")]
    [WholeNumber]
    public decimal Quantity { get; init; }
}
