using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.EmailTemplates;
using Project1.Api.Entities;

namespace Project1.Api.Email;

public sealed partial class EmailTemplateRenderer(AppDbContext dbContext) : IEmailTemplateRenderer
{
    private static readonly HashSet<string> SupportedPlaceholders =
        EmailTemplateConstants.PurchaseOrderPlaceholders.ToHashSet(StringComparer.Ordinal);

    public string? Validate(
        string subjectTemplate,
        string htmlBodyTemplate,
        string? ccRecipients,
        string? bccRecipients)
    {
        var subject = subjectTemplate.Trim();
        var body = htmlBodyTemplate.Trim();

        if (subject.Length is < 1 or > 300)
        {
            return "Email subject must contain between 1 and 300 characters.";
        }

        if (body.Length is < 1 or > 50000)
        {
            return "Email HTML body must contain between 1 and 50,000 characters.";
        }

        var subjectError = ValidatePlaceholders(subject, allowItemsTable: false);
        if (subjectError is not null)
        {
            return subjectError;
        }

        var bodyError = ValidatePlaceholders(body, allowItemsTable: true);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var ccError = ValidateRecipientList(ccRecipients, "CC");
        if (ccError is not null)
        {
            return ccError;
        }

        return ValidateRecipientList(bccRecipients, "BCC");
    }

    public EmailTemplatePreviewResponse RenderPreview(
        string subjectTemplate,
        string htmlBodyTemplate,
        string? ccRecipients,
        string? bccRecipients)
    {
        var values = new PurchaseOrderEmailValues(
            "PO-0001",
            "ABC Office Supplies",
            "orders@abc-supplier.example",
            "29 Sep 2026",
            "13 Oct 2026",
            "Main Warehouse, Kuala Lumpur",
            "ABC-Q-2026-001",
            BuildSampleItemsTable(),
            "RM 2,399.90",
            "Please deliver during office hours.");

        return new EmailTemplatePreviewResponse(
            values.SupplierEmail,
            NormalizeRecipientList(ccRecipients),
            NormalizeRecipientList(bccRecipients),
            RenderText(subjectTemplate.Trim(), values, encodeForHtml: false),
            RenderText(htmlBodyTemplate.Trim(), values, encodeForHtml: true));
    }

    public async Task<EmailTemplateRenderResult> RenderPurchaseOrderAsync(
        PurchaseOrder purchaseOrder,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.EmailTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == EmailTemplateConstants.PurchaseOrderIssuedCode &&
                        item.Status == EmailTemplateStatus.Active,
                cancellationToken);

        if (template is null)
        {
            return Failed(
                "No active Purchase Order email template is available. Ask an administrator to publish one before issuing this purchase order.");
        }

        var validationError = Validate(
            template.SubjectTemplate,
            template.HtmlBodyTemplate,
            template.CcRecipients,
            template.BccRecipients);
        if (validationError is not null)
        {
            return Failed($"The active Purchase Order email template is invalid: {validationError}");
        }

        var total = purchaseOrder.Items.Sum(item => item.Quantity * item.UnitPrice);
        var values = new PurchaseOrderEmailValues(
            purchaseOrder.PurchaseOrderNumber,
            purchaseOrder.SupplierName,
            recipientEmail,
            purchaseOrder.OrderDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            purchaseOrder.ExpectedDeliveryDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                ?? "Not specified",
            ValueOrFallback(purchaseOrder.DeliveryAddress),
            ValueOrFallback(purchaseOrder.SupplierQuotationReference),
            BuildItemsTable(purchaseOrder.Items),
            $"RM {total:N2}",
            ValueOrFallback(purchaseOrder.Notes));

        var message = new EmailMessage(
            recipientEmail,
            RenderText(template.SubjectTemplate, values, encodeForHtml: false),
            RenderText(template.HtmlBodyTemplate, values, encodeForHtml: true),
            CcRecipients: NormalizeRecipientList(template.CcRecipients),
            BccRecipients: NormalizeRecipientList(template.BccRecipients));

        return new EmailTemplateRenderResult(
            true,
            new RenderedEmailTemplate(template.Id, template.Code, template.Version, message));
    }

    private static string? ValidatePlaceholders(string value, bool allowItemsTable)
    {
        var remaining = PlaceholderPattern().Replace(value, string.Empty);
        if (remaining.Contains("{{", StringComparison.Ordinal) ||
            remaining.Contains("}}", StringComparison.Ordinal))
        {
            return "The template contains a malformed placeholder. Use the format {{PlaceholderName}}.";
        }

        foreach (Match match in PlaceholderPattern().Matches(value))
        {
            var placeholder = match.Groups[1].Value;
            if (!SupportedPlaceholders.Contains(placeholder))
            {
                return $"Unknown placeholder '{{{{{placeholder}}}}}'.";
            }

            if (!allowItemsTable && placeholder == "ItemsTable")
            {
                return "{{ItemsTable}} can only be used in the HTML body.";
            }
        }

        return null;
    }

    private static string? ValidateRecipientList(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var recipient in SplitRecipients(value))
        {
            if (!MailAddress.TryCreate(recipient, out _))
            {
                return $"{fieldName} contains an invalid email address: {recipient}.";
            }
        }

        return null;
    }

    private static string? NormalizeRecipientList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(", ", SplitRecipients(value).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SplitRecipients(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string RenderText(
        string template,
        PurchaseOrderEmailValues values,
        bool encodeForHtml)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PurchaseOrderNumber"] = EncodeIfNeeded(values.PurchaseOrderNumber, encodeForHtml),
            ["SupplierName"] = EncodeIfNeeded(values.SupplierName, encodeForHtml),
            ["SupplierEmail"] = EncodeIfNeeded(values.SupplierEmail, encodeForHtml),
            ["OrderDate"] = EncodeIfNeeded(values.OrderDate, encodeForHtml),
            ["ExpectedDeliveryDate"] = EncodeIfNeeded(values.ExpectedDeliveryDate, encodeForHtml),
            ["DeliveryAddress"] = EncodeIfNeeded(values.DeliveryAddress, encodeForHtml),
            ["SupplierQuotationReference"] = EncodeIfNeeded(
                values.SupplierQuotationReference,
                encodeForHtml),
            ["ItemsTable"] = encodeForHtml ? values.ItemsTable : "Ordered items",
            ["TotalAmount"] = EncodeIfNeeded(values.TotalAmount, encodeForHtml),
            ["Notes"] = EncodeIfNeeded(values.Notes, encodeForHtml)
        };

        return PlaceholderPattern().Replace(
            template,
            match => replacements[match.Groups[1].Value]);
    }

    private static string EncodeIfNeeded(string value, bool encodeForHtml) =>
        encodeForHtml ? WebUtility.HtmlEncode(value) : value;

    private static string BuildItemsTable(IEnumerable<PurchaseOrderItem> purchaseOrderItems)
    {
        var items = new StringBuilder();
        foreach (var item in purchaseOrderItems.OrderBy(item => item.Id))
        {
            var lineTotal = item.Quantity * item.UnitPrice;
            items.Append(CultureInfo.InvariantCulture, $"""
                <tr>
                  <td>{WebUtility.HtmlEncode(item.ProductCode)} - {WebUtility.HtmlEncode(item.ProductName)}</td>
                  <td style="text-align:right">{item.Quantity:0.###} {WebUtility.HtmlEncode(item.UnitOfMeasureCode)}</td>
                  <td style="text-align:right">RM {item.UnitPrice:N2}</td>
                  <td style="text-align:right">RM {lineTotal:N2}</td>
                </tr>
                """);
        }

        return $"""
            <table style="width:100%;border-collapse:collapse" border="1" cellpadding="8">
              <thead><tr><th align="left">Item</th><th>Quantity</th><th>Unit price</th><th>Total</th></tr></thead>
              <tbody>{items}</tbody>
            </table>
            """;
    }

    private static string BuildSampleItemsTable() => """
        <table style="width:100%;border-collapse:collapse" border="1" cellpadding="8">
          <thead><tr><th align="left">Item</th><th>Quantity</th><th>Unit price</th><th>Total</th></tr></thead>
          <tbody>
            <tr><td>ITEM-0001 - USB-C Monitor</td><td style="text-align:right">1 UNIT</td><td style="text-align:right">RM 1,399.90</td><td style="text-align:right">RM 1,399.90</td></tr>
            <tr><td>ITEM-0002 - Office Chair</td><td style="text-align:right">2 UNIT</td><td style="text-align:right">RM 500.00</td><td style="text-align:right">RM 1,000.00</td></tr>
          </tbody>
        </table>
        """;

    private static string ValueOrFallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not provided" : value.Trim();

    private static EmailTemplateRenderResult Failed(string message) => new(false, ErrorMessage: message);

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    private sealed record PurchaseOrderEmailValues(
        string PurchaseOrderNumber,
        string SupplierName,
        string SupplierEmail,
        string OrderDate,
        string ExpectedDeliveryDate,
        string DeliveryAddress,
        string SupplierQuotationReference,
        string ItemsTable,
        string TotalAmount,
        string Notes);
}
