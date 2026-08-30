using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.Services.PurchaseRequests;

namespace Project1.Api.Tests.Controllers;

public sealed class PurchaseRequestsControllerTests
{
    [Fact]
    public async Task Update_ReturnsConflict_WhenRequestIsNotDraft()
    {
        var service = new FakePurchaseRequestService
        {
            Result = new PurchaseRequestOperationResult(
                PurchaseRequestOperationStatus.InvalidState,
                ErrorMessage: "Only purchase requests at the DRAFT step can be edited.")
        };
        var controller = new PurchaseRequestsController(service);

        var response = await controller.Update(
            1,
            new UpdatePurchaseRequestRequest(),
            CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("Only purchase requests at the DRAFT step can be edited.", problem.Detail);
    }

    [Fact]
    public async Task ExecuteAction_ReturnsBadRequest_WhenCommentIsRequired()
    {
        var service = new FakePurchaseRequestService
        {
            Result = new PurchaseRequestOperationResult(
                PurchaseRequestOperationStatus.ValidationFailed,
                ErrorMessage: "A comment is required for action 'REJECT'.")
        };
        var controller = new PurchaseRequestsController(service);

        var response = await controller.ExecuteAction(
            1,
            "REJECT",
            new PurchaseRequestActionRequest(),
            CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("A comment is required for action 'REJECT'.", problem.Detail);
        Assert.Equal("REJECT", service.LastActionCode);
    }

    [Fact]
    public async Task ExecuteAction_ReturnsForbidden_WhenActorIsNotAuthorized()
    {
        var service = new FakePurchaseRequestService
        {
            Result = new PurchaseRequestOperationResult(
                PurchaseRequestOperationStatus.Unauthorized,
                ErrorMessage: "The actor is not authorized.")
        };
        var controller = new PurchaseRequestsController(service);

        var response = await controller.ExecuteAction(
            1,
            "APPROVE",
            new PurchaseRequestActionRequest(),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(forbidden.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal("The actor is not authorized.", problem.Detail);
    }

    private sealed class FakePurchaseRequestService : IPurchaseRequestService
    {
        public PurchaseRequestOperationResult Result { get; init; } =
            new(PurchaseRequestOperationStatus.NotFound);

        public string? LastActionCode { get; private set; }

        public Task<IReadOnlyList<PurchaseRequestResponse>> GetAllAsync(
            string? stepCode,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PurchaseRequestResponse>>([]);

        public Task<PurchaseRequestResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<PurchaseRequestResponse?>(null);

        public Task<PurchaseRequestOperationResult> CreateAsync(
            CreatePurchaseRequestRequest request,
            CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<PurchaseRequestOperationResult> UpdateAsync(
            int id,
            UpdatePurchaseRequestRequest request,
            CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<PurchaseRequestOperationResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(Result);

        public Task<PurchaseRequestOperationResult> ExecuteActionAsync(
            int id,
            string actionCode,
            PurchaseRequestActionRequest request,
            CancellationToken cancellationToken)
        {
            LastActionCode = actionCode;
            return Task.FromResult(Result);
        }
    }
}
