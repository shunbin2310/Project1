using System.Text;
using Project1.Api.Email;
using Project1.Api.Entities;

namespace Project1.Api.Tests.Email;

public sealed class PurchaseOrderPdfGeneratorTests
{
    [Fact]
    public void Generate_CreatesNonEmptyPdfDocument()
    {
        var order = new PurchaseOrder
        {
            PurchaseOrderNumber = "PO-0099",
            PurchaseRequestNumber = "PR-0099",
            QuotationNumber = "QT-0099",
            SupplierCode = "SUP-0099",
            SupplierName = "Example Supplier",
            SupplierQuotationReference = "SUP-Q-0099",
            Supplier = new Supplier
            {
                Code = "SUP-0099",
                Name = "Example Supplier",
                ContactPerson = "Supplier Contact",
                Email = "supplier@example.test",
                Address = "Example supplier address"
            },
            OrderDate = new DateOnly(2026, 9, 30),
            ExpectedDeliveryDate = new DateOnly(2026, 10, 15),
            DeliveryAddress = "Main warehouse",
            Notes = "Deliver during office hours.",
            Items =
            [
                new PurchaseOrderItem
                {
                    Id = 1,
                    ProductCode = "ITEM-0001",
                    ProductName = "Monitor",
                    UnitOfMeasureCode = "UNIT",
                    Quantity = 2,
                    UnitPrice = 1399.90m
                }
            ]
        };

        var content = new PurchaseOrderPdfGenerator().Generate(
            order,
            "Demo Procurement",
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

        Assert.True(content.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(content, 0, 4));
    }
}
