using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.GoodsReceipts;
using Project1.Api.Entities;
using Project1.Api.Services.GoodsReceipts;

namespace Project1.Api.Tests.Controllers;

public sealed class GoodsReceiptsControllerTests
{
    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenReceiptIsCreated()
    {
        var controller = new GoodsReceiptsController(new FakeGoodsReceiptService());

        var response = await controller.Create(
            new CreateGoodsReceiptRequest(),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var receipt = Assert.IsType<GoodsReceiptResponse>(created.Value);
        Assert.Equal("GRN-0001", receipt.GoodsReceiptNumber);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenDraftAlreadyExists()
    {
        var service = new FakeGoodsReceiptService
        {
            CreateResult = new GoodsReceiptOperationResult(
                GoodsReceiptOperationStatus.DuplicateDraft,
                ErrorMessage: "Complete or delete the existing draft receipt first.")
        };
        var controller = new GoodsReceiptsController(service);

        var response = await controller.Create(
            new CreateGoodsReceiptRequest(),
            CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("A draft goods receipt already exists.", problem.Title);
    }

    [Fact]
    public async Task Post_ReturnsConflict_WhenQuantityExceedsOrder()
    {
        var service = new FakeGoodsReceiptService
        {
            PostResult = new GoodsReceiptOperationResult(
                GoodsReceiptOperationStatus.QuantityExceeded,
                ErrorMessage: "Received quantity exceeds the remaining order quantity.")
        };
        var controller = new GoodsReceiptsController(service);

        var response = await controller.Post(1, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Received quantity exceeds the purchase order.", problem.Title);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenDraftIsDeleted()
    {
        var controller = new GoodsReceiptsController(new FakeGoodsReceiptService());

        var response = await controller.Delete(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    private static GoodsReceiptResponse CreateResponse() => new(
        1,
        "GRN-0001",
        1,
        "PO-0001",
        1,
        "SUP-0001",
        "Supplier One",
        "DN-001",
        new DateOnly(2026, 9, 16),
        null,
        GoodsReceiptStatus.Draft,
        4,
        "Demo Admin",
        DateTimeOffset.UtcNow,
        null,
        null,
        null,
        null,
        []);

    private sealed class FakeGoodsReceiptService : IGoodsReceiptService
    {
        public GoodsReceiptOperationResult CreateResult { get; init; } =
            new(GoodsReceiptOperationStatus.Success, CreateResponse());

        public GoodsReceiptOperationResult UpdateResult { get; init; } =
            new(GoodsReceiptOperationStatus.Success, CreateResponse());

        public GoodsReceiptOperationResult PostResult { get; init; } =
            new(GoodsReceiptOperationStatus.Success, CreateResponse());

        public GoodsReceiptOperationResult DeleteResult { get; init; } =
            new(GoodsReceiptOperationStatus.Success);

        public Task<IReadOnlyList<GoodsReceiptResponse>> GetAllAsync(
            int? purchaseOrderId,
            int? supplierId,
            GoodsReceiptStatus? status,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoodsReceiptResponse>>([]);

        public Task<GoodsReceiptResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<GoodsReceiptResponse?>(CreateResponse());

        public Task<GoodsReceiptOperationResult> CreateAsync(
            CreateGoodsReceiptRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<GoodsReceiptOperationResult> UpdateAsync(
            int id,
            UpdateGoodsReceiptRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<GoodsReceiptOperationResult> PostAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(PostResult);

        public Task<GoodsReceiptOperationResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeleteResult);
    }
}
