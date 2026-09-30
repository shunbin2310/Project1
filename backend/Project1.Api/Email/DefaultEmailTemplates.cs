namespace Project1.Api.Email;

public static class DefaultEmailTemplates
{
    public const string PurchaseOrderSubject = "Purchase Order {{PurchaseOrderNumber}}";

    public const string PurchaseOrderHtmlBody = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>{{PurchaseOrderNumber}}</title></head>
        <body style="font-family:Arial,sans-serif;color:#17312f;line-height:1.5">
          <div style="max-width:760px;margin:auto;padding:24px">
            <p style="color:#14766d;font-weight:700;text-transform:uppercase">Purchase Order</p>
            <h1>{{PurchaseOrderNumber}}</h1>
            <p>Dear {{SupplierName}},</p>
            <p>Please process the purchase order below.</p>
            <table style="width:100%;border-collapse:collapse;margin:20px 0">
              <tr><td><strong>Order date</strong></td><td>{{OrderDate}}</td></tr>
              <tr><td><strong>Expected delivery</strong></td><td>{{ExpectedDeliveryDate}}</td></tr>
              <tr><td><strong>Delivery address</strong></td><td>{{DeliveryAddress}}</td></tr>
              <tr><td><strong>Reference</strong></td><td>{{SupplierQuotationReference}}</td></tr>
            </table>
            {{ItemsTable}}
            <p><strong>Grand total:</strong> {{TotalAmount}}</p>
            <p><strong>Notes:</strong> {{Notes}}</p>
            <p>Regards,<br>Project1 Purchasing</p>
          </div>
        </body>
        </html>
        """;
}
