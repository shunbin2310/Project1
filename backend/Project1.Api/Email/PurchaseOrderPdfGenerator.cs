using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Project1.Api.Entities;

namespace Project1.Api.Email;

public sealed class PurchaseOrderPdfGenerator : IPurchaseOrderPdfGenerator
{
    private static readonly object FontResolverLock = new();

    public PurchaseOrderPdfGenerator()
    {
        lock (FontResolverLock)
        {
            GlobalFontSettings.FontResolver ??= new CrossPlatformFontResolver();
        }
    }

    public byte[] Generate(
        PurchaseOrder purchaseOrder,
        string issuedByName,
        DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(purchaseOrder);

        var document = BuildDocument(purchaseOrder, issuedByName, issuedAtUtc);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static Document BuildDocument(
        PurchaseOrder purchaseOrder,
        string issuedByName,
        DateTimeOffset issuedAtUtc)
    {
        var document = new Document
        {
            Info =
            {
                Title = $"Purchase Order {purchaseOrder.PurchaseOrderNumber}",
                Author = "Project1 Purchasing"
            }
        };

        var normalStyle = document.Styles[StyleNames.Normal]!;
        normalStyle.Font.Name = "Project1 Sans";
        normalStyle.Font.Size = 9;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

        var title = section.AddParagraph();
        title.AddFormattedText("PROJECT1 PURCHASING", TextFormat.Bold);
        title.Format.Font.Size = 18;
        title.Format.Font.Color = Color.FromCmyk(100, 30, 60, 30);
        title.Format.SpaceAfter = Unit.FromPoint(2);

        var subtitle = section.AddParagraph();
        subtitle.AddFormattedText("PURCHASE ORDER", TextFormat.Bold);
        subtitle.Format.Font.Size = 12;
        subtitle.Format.SpaceAfter = Unit.FromCentimeter(0.5);

        var summary = section.AddTable();
        summary.Borders.Width = 0.5;
        summary.Borders.Color = Colors.LightGray;
        summary.AddColumn(Unit.FromCentimeter(4));
        summary.AddColumn(Unit.FromCentimeter(5));
        summary.AddColumn(Unit.FromCentimeter(4));
        summary.AddColumn(Unit.FromCentimeter(5));
        AddSummaryRow(summary, "PO number", purchaseOrder.PurchaseOrderNumber, "Order date", purchaseOrder.OrderDate.ToString("dd MMM yyyy"));
        AddSummaryRow(summary, "Purchase request", purchaseOrder.PurchaseRequestNumber, "Expected delivery", purchaseOrder.ExpectedDeliveryDate?.ToString("dd MMM yyyy") ?? "Not specified");
        AddSummaryRow(summary, "Quotation", purchaseOrder.QuotationNumber, "Supplier reference", purchaseOrder.SupplierQuotationReference ?? "Not provided");

        AddHeading(section, "Supplier");
        AddText(section, $"{purchaseOrder.SupplierName} ({purchaseOrder.SupplierCode})");
        if (!string.IsNullOrWhiteSpace(purchaseOrder.Supplier.ContactPerson))
        {
            AddText(section, $"Contact: {purchaseOrder.Supplier.ContactPerson}");
        }
        if (!string.IsNullOrWhiteSpace(purchaseOrder.Supplier.Email))
        {
            AddText(section, $"Email: {purchaseOrder.Supplier.Email}");
        }
        if (!string.IsNullOrWhiteSpace(purchaseOrder.Supplier.Address))
        {
            AddText(section, $"Address: {purchaseOrder.Supplier.Address}");
        }

        AddHeading(section, "Delivery");
        AddText(section, purchaseOrder.DeliveryAddress ?? "Not specified");

        AddHeading(section, "Order items");
        var items = section.AddTable();
        items.Borders.Width = 0.5;
        items.Borders.Color = Colors.LightGray;
        items.Rows.LeftIndent = 0;
        items.AddColumn(Unit.FromCentimeter(2.6));
        items.AddColumn(Unit.FromCentimeter(6));
        items.AddColumn(Unit.FromCentimeter(2.6));
        items.AddColumn(Unit.FromCentimeter(3));
        items.AddColumn(Unit.FromCentimeter(3.3));

        var header = items.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Color.FromRgb(229, 241, 238);
        SetCell(header.Cells[0], "Code", bold: true);
        SetCell(header.Cells[1], "Product", bold: true);
        SetCell(header.Cells[2], "Quantity", bold: true);
        SetCell(header.Cells[3], "Unit price", bold: true);
        SetCell(header.Cells[4], "Line total", bold: true);

        foreach (var item in purchaseOrder.Items.OrderBy(item => item.Id))
        {
            var row = items.AddRow();
            SetCell(row.Cells[0], item.ProductCode);
            SetCell(row.Cells[1], item.ProductName);
            SetCell(row.Cells[2], $"{item.Quantity:0.###} {item.UnitOfMeasureCode}", ParagraphAlignment.Right);
            SetCell(row.Cells[3], $"RM {item.UnitPrice:N2}", ParagraphAlignment.Right);
            SetCell(row.Cells[4], $"RM {item.Quantity * item.UnitPrice:N2}", ParagraphAlignment.Right);
        }

        var totalRow = items.AddRow();
        totalRow.Shading.Color = Color.FromRgb(242, 247, 246);
        totalRow.Cells[0].MergeRight = 3;
        SetCell(totalRow.Cells[0], "TOTAL AMOUNT", ParagraphAlignment.Right, bold: true);
        SetCell(
            totalRow.Cells[4],
            $"RM {purchaseOrder.Items.Sum(item => item.Quantity * item.UnitPrice):N2}",
            ParagraphAlignment.Right,
            bold: true);

        if (!string.IsNullOrWhiteSpace(purchaseOrder.Notes))
        {
            AddHeading(section, "Notes");
            AddText(section, purchaseOrder.Notes);
        }

        var footer = section.AddParagraph();
        footer.Format.SpaceBefore = Unit.FromCentimeter(0.8);
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Colors.Gray;
        footer.AddText($"Issued by {issuedByName} on {issuedAtUtc.ToLocalTime():dd MMM yyyy, h:mm tt}.");

        return document;
    }

    private static void AddSummaryRow(
        Table table,
        string firstLabel,
        string firstValue,
        string secondLabel,
        string secondValue)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(5);
        row.BottomPadding = Unit.FromPoint(5);
        SetCell(row.Cells[0], firstLabel, bold: true);
        SetCell(row.Cells[1], firstValue);
        SetCell(row.Cells[2], secondLabel, bold: true);
        SetCell(row.Cells[3], secondValue);
    }

    private static void AddHeading(Section section, string text)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.SpaceBefore = Unit.FromCentimeter(0.5);
        paragraph.Format.SpaceAfter = Unit.FromPoint(3);
        paragraph.AddFormattedText(text, TextFormat.Bold);
    }

    private static void AddText(Section section, string text)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void SetCell(
        Cell cell,
        string text,
        ParagraphAlignment alignment = ParagraphAlignment.Left,
        bool bold = false)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = alignment;
        paragraph.AddFormattedText(text, bold ? TextFormat.Bold : TextFormat.NotBold);
    }
}
