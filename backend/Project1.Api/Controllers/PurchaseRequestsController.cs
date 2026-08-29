using Microsoft.AspNetCore.Mvc;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.Services.PurchaseRequests;

namespace Project1.Api.Controllers;

[ApiController]
[Route("api/purchase-requests")]
public sealed class PurchaseRequestsController(IPurchaseRequestService purchaseRequestService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PurchaseRequestResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PurchaseRequestResponse>>> GetAll(
        [FromQuery] string? stepCode = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await purchaseRequestService.GetAllAsync(stepCode, cancellationToken));
    }

    [HttpGet("{id:int}", Name = "GetPurchaseRequestById")]
    [ProducesResponseType<PurchaseRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseRequestResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var request = await purchaseRequestService.GetByIdAsync(id, cancellationToken);
        return request is null ? NotFound() : Ok(request);
    }

    [HttpPost]
    [ProducesResponseType<PurchaseRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PurchaseRequestResponse>> Create(
        CreatePurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseRequestService.CreateAsync(request, cancellationToken);

        if (result.Status != PurchaseRequestOperationStatus.Success)
        {
            return OperationProblem(result);
        }

        var purchaseRequest = result.PurchaseRequest!;
        return CreatedAtAction(
            nameof(GetById),
            new { id = purchaseRequest.Id },
            purchaseRequest);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<PurchaseRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseRequestResponse>> Update(
        int id,
        UpdatePurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        return OperationResponse(
            await purchaseRequestService.UpdateAsync(id, request, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await purchaseRequestService.DeleteAsync(id, cancellationToken);

        if (result.Status == PurchaseRequestOperationStatus.Success)
        {
            return NoContent();
        }

        return OperationProblem(result).Result!;
    }

    [HttpPost("{id:int}/actions/{actionCode}")]
    [ProducesResponseType<PurchaseRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseRequestResponse>> ExecuteAction(
        int id,
        string actionCode,
        PurchaseRequestActionRequest request,
        CancellationToken cancellationToken)
    {
        return OperationResponse(
            await purchaseRequestService.ExecuteActionAsync(
                id,
                actionCode,
                request,
                cancellationToken));
    }

    private ActionResult<PurchaseRequestResponse> OperationResponse(
        PurchaseRequestOperationResult result)
    {
        return result.Status == PurchaseRequestOperationStatus.Success
            ? Ok(result.PurchaseRequest)
            : OperationProblem(result);
    }

    private ActionResult<PurchaseRequestResponse> OperationProblem(
        PurchaseRequestOperationResult result)
    {
        if (result.Status == PurchaseRequestOperationStatus.NotFound)
        {
            return NotFound();
        }

        var statusCode = result.Status switch
        {
            PurchaseRequestOperationStatus.InvalidState => StatusCodes.Status409Conflict,
            PurchaseRequestOperationStatus.Unauthorized => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = result.Status switch
            {
                PurchaseRequestOperationStatus.InvalidState =>
                    "Workflow action is not available.",
                PurchaseRequestOperationStatus.Unauthorized =>
                    "Workflow action is not authorized.",
                PurchaseRequestOperationStatus.WorkflowUnavailable =>
                    "Workflow is unavailable.",
                _ => "Purchase request validation failed."
            },
            Detail = result.ErrorMessage
        };

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
