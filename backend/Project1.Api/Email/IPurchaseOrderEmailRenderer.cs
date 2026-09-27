using Project1.Api.Entities;

namespace Project1.Api.Email;

public interface IPurchaseOrderEmailRenderer
{
    EmailMessage Render(PurchaseOrder purchaseOrder, string recipientEmail);
}
