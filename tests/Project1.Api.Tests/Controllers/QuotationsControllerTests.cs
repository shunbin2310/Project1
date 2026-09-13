using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Quotations;
using Project1.Api.Entities;
using Project1.Api.Services.Quotations;

namespace Project1.Api.Tests.Controllers;

public sealed class QuotationsControllerTests
{
    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenQuotationIsCreated()
    {
        var controller = new QuotationsController(new FakeQuotationService());

        var response = await controller.Create(
            new CreateQuotationRequest(),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var quotation = Assert.IsType<QuotationResponse>(created.Value);
        Assert.Equal("QT-0001", quotation.QuotationNumber);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenSupplierCannotSupplyProducts()
    {
        var service = new FakeQuotationService
        {
            CreateResult = new QuotationOperationResult(
                QuotationOperationStatus.SupplierCannotSupplyProducts,
                ErrorMessage: "The supplier is not active for: ITEM-0002.")
        };
        var controller = new QuotationsController(service);

        var response = await controller.Create(
            new CreateQuotationRequest(),
            CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Supplier cannot supply this request.", problem.Title);
    }

    [Fact]
    public async Task Submit_ReturnsConflict_WhenQuotationStateIsInvalid()
    {
        var service = new FakeQuotationService
        {
            SubmitResult = new QuotationOperationResult(
                QuotationOperationStatus.InvalidState,
                ErrorMessage: "Only draft quotations can be submitted.")
        };
        var controller = new QuotationsController(service);

        var response = await controller.Submit(1, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Quotation operation is not allowed.", problem.Title);
    }

    [Fact]
    public async Task GetComparison_ReturnsNotFound_WhenPurchaseRequestDoesNotExist()
    {
        var service = new FakeQuotationService { Comparison = null };
        var controller = new QuotationsController(service);

        var response = await controller.GetComparison(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    private static QuotationResponse CreateResponse() => new(
        1,
        "QT-0001",
        1,
        "PR-0001",
        1,
        "SUP-0001",
        "Supplier One",
        "SUP-Q-001",
        new DateOnly(2026, 9, 12),
        new DateOnly(2026, 10, 12),
        null,
        QuotationStatus.Draft,
        200m,
        4,
        "Demo Admin",
        DateTimeOffset.UtcNow,
        null,
        null,
        null,
        []);

    private sealed class FakeQuotationService : IQuotationService
    {
        public QuotationOperationResult CreateResult { get; init; } =
            new(QuotationOperationStatus.Success, CreateResponse());

        public QuotationOperationResult UpdateResult { get; init; } =
            new(QuotationOperationStatus.Success, CreateResponse());

        public QuotationOperationResult SubmitResult { get; init; } =
            new(QuotationOperationStatus.Success, CreateResponse());

        public QuotationOperationResult SelectResult { get; init; } =
            new(QuotationOperationStatus.Success, CreateResponse());

        public QuotationOperationResult DeleteResult { get; init; } =
            new(QuotationOperationStatus.Success);

        public QuotationComparisonResponse? Comparison { get; init; } = new(
            1,
            "PR-0001",
            null,
            null,
            []);

        public Task<IReadOnlyList<QuotationResponse>> GetAllAsync(
            int? purchaseRequestId,
            QuotationStatus? status,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuotationResponse>>([]);

        public Task<QuotationResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<QuotationResponse?>(CreateResponse());

        public Task<QuotationComparisonResponse?> GetComparisonAsync(
            int purchaseRequestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Comparison);

        public Task<QuotationOperationResult> CreateAsync(
            CreateQuotationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<QuotationOperationResult> UpdateAsync(
            int id,
            UpdateQuotationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<QuotationOperationResult> SubmitAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(SubmitResult);

        public Task<QuotationOperationResult> SelectAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(SelectResult);

        public Task<QuotationOperationResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeleteResult);
    }
}
