using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.Entities;
using Project1.Api.Services.PurchaseOrders;

namespace Project1.Api.Tests.Controllers;

public sealed class PurchaseOrdersControllerTests
{
    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenPurchaseOrderIsCreated()
    {
        var controller = new PurchaseOrdersController(new FakePurchaseOrderService());

        var response = await controller.Create(
            new CreatePurchaseOrderRequest(),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var purchaseOrder = Assert.IsType<PurchaseOrderResponse>(created.Value);
        Assert.Equal("PO-0001", purchaseOrder.PurchaseOrderNumber);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenQuotationIsNotSelected()
    {
        var service = new FakePurchaseOrderService
        {
            CreateResult = new PurchaseOrderOperationResult(
                PurchaseOrderOperationStatus.QuotationNotSelected,
                ErrorMessage: "Only a selected quotation can be converted into a purchase order.")
        };
        var controller = new PurchaseOrdersController(service);

        var response = await controller.Create(
            new CreatePurchaseOrderRequest(),
            CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Quotation has not been selected.", problem.Title);
    }

    [Fact]
    public async Task Issue_ReturnsBadRequest_WhenRequiredDetailsAreMissing()
    {
        var service = new FakePurchaseOrderService
        {
            IssueResult = new PurchaseOrderOperationResult(
                PurchaseOrderOperationStatus.ValidationFailed,
                ErrorMessage: "Delivery address is required before issue.")
        };
        var controller = new PurchaseOrdersController(service);

        var response = await controller.Issue(1, CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Purchase order validation failed.", problem.Title);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenDraftIsDeleted()
    {
        var controller = new PurchaseOrdersController(new FakePurchaseOrderService());

        var response = await controller.Delete(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    private static PurchaseOrderResponse CreateResponse() => new(
        1,
        "PO-0001",
        1,
        "QT-0001",
        1,
        "PR-0001",
        1,
        "SUP-0001",
        "Supplier One",
        "SUP-Q-001",
        new DateOnly(2026, 9, 13),
        new DateOnly(2026, 9, 30),
        "Main warehouse",
        null,
        PurchaseOrderStatus.Draft,
        200m,
        4,
        "Demo Admin",
        DateTimeOffset.UtcNow,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        []);

    private sealed class FakePurchaseOrderService : IPurchaseOrderService
    {
        public PurchaseOrderOperationResult CreateResult { get; init; } =
            new(PurchaseOrderOperationStatus.Success, CreateResponse());

        public PurchaseOrderOperationResult UpdateResult { get; init; } =
            new(PurchaseOrderOperationStatus.Success, CreateResponse());

        public PurchaseOrderOperationResult IssueResult { get; init; } =
            new(PurchaseOrderOperationStatus.Success, CreateResponse());

        public PurchaseOrderOperationResult CancelResult { get; init; } =
            new(PurchaseOrderOperationStatus.Success, CreateResponse());

        public PurchaseOrderOperationResult DeleteResult { get; init; } =
            new(PurchaseOrderOperationStatus.Success);

        public Task<IReadOnlyList<PurchaseOrderResponse>> GetAllAsync(
            int? supplierId,
            PurchaseOrderStatus? status,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PurchaseOrderResponse>>([]);

        public Task<PurchaseOrderResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<PurchaseOrderResponse?>(CreateResponse());

        public Task<PurchaseOrderOperationResult> CreateAsync(
            CreatePurchaseOrderRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<PurchaseOrderOperationResult> UpdateAsync(
            int id,
            UpdatePurchaseOrderRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<PurchaseOrderOperationResult> IssueAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(IssueResult);

        public Task<PurchaseOrderOperationResult> CancelAsync(
            int id,
            CancelPurchaseOrderRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CancelResult);

        public Task<PurchaseOrderOperationResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeleteResult);
    }
}
