using Project1.Api.Entities;

namespace Project1.Api.Email;

public interface IPurchaseOrderPdfGenerator
{
    byte[] Generate(
        PurchaseOrder purchaseOrder,
        string issuedByName,
        DateTimeOffset issuedAtUtc);
}
