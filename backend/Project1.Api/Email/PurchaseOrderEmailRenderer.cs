using System.Globalization;
using System.Net;
using System.Text;
using Project1.Api.Entities;

namespace Project1.Api.Email;

public sealed class PurchaseOrderEmailRenderer : IPurchaseOrderEmailRenderer
{
    public EmailMessage Render(PurchaseOrder purchaseOrder, string recipientEmail)
    {
        var items = new StringBuilder();
        foreach (var item in purchaseOrder.Items.OrderBy(item => item.Id))
        {
            var lineTotal = item.Quantity * item.UnitPrice;
            items.Append(CultureInfo.InvariantCulture, $"""
                <tr>
                  <td>{Encode(item.ProductCode)} - {Encode(item.ProductName)}</td>
                  <td style="text-align:right">{item.Quantity:0.###} {Encode(item.UnitOfMeasureCode)}</td>
                  <td style="text-align:right">RM {item.UnitPrice:N2}</td>
                  <td style="text-align:right">RM {lineTotal:N2}</td>
                </tr>
                """);
        }

        var total = purchaseOrder.Items.Sum(item => item.Quantity * item.UnitPrice);
        var expectedDelivery = purchaseOrder.ExpectedDeliveryDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            ?? "Not specified";
        var body = $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>{{Encode(purchaseOrder.PurchaseOrderNumber)}}</title></head>
            <body style="font-family:Arial,sans-serif;color:#17312f;line-height:1.5">
              <div style="max-width:760px;margin:auto;padding:24px">
                <p style="color:#14766d;font-weight:700;text-transform:uppercase">Purchase Order</p>
                <h1>{{Encode(purchaseOrder.PurchaseOrderNumber)}}</h1>
                <p>Dear {{Encode(purchaseOrder.SupplierName)}},</p>
                <p>Please process the purchase order below.</p>
                <table style="width:100%;border-collapse:collapse;margin:20px 0">
                  <tr><td><strong>Order date</strong></td><td>{{purchaseOrder.OrderDate:dd MMM yyyy}}</td></tr>
                  <tr><td><strong>Expected delivery</strong></td><td>{{expectedDelivery}}</td></tr>
                  <tr><td><strong>Delivery address</strong></td><td>{{Encode(purchaseOrder.DeliveryAddress)}}</td></tr>
                  <tr><td><strong>Reference</strong></td><td>{{Encode(purchaseOrder.SupplierQuotationReference)}}</td></tr>
                </table>
                <table style="width:100%;border-collapse:collapse" border="1" cellpadding="8">
                  <thead><tr><th align="left">Item</th><th>Quantity</th><th>Unit price</th><th>Total</th></tr></thead>
                  <tbody>{{items}}</tbody>
                  <tfoot><tr><td colspan="3" align="right"><strong>Grand total</strong></td><td align="right"><strong>RM {{total:N2}}</strong></td></tr></tfoot>
                </table>
                <p><strong>Notes:</strong> {{Encode(purchaseOrder.Notes)}}</p>
                <p>Regards,<br>Project1 Purchasing</p>
              </div>
            </body>
            </html>
            """;

        return new EmailMessage(
            recipientEmail,
            $"Purchase Order {purchaseOrder.PurchaseOrderNumber}",
            body);
    }

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "Not provided" : value.Trim());
}
