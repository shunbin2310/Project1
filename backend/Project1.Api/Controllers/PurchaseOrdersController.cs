using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.Entities;
using Project1.Api.Services.PurchaseOrders;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController(
    IPurchaseOrderService purchaseOrderService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PurchaseOrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PurchaseOrderResponse>>> GetAll(
        [FromQuery] int? supplierId = null,
        [FromQuery] PurchaseOrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var purchaseOrders = await purchaseOrderService.GetAllAsync(
            supplierId,
            status,
            cancellationToken);

        return Ok(purchaseOrders);
    }

    [HttpGet("{id:int}", Name = "GetPurchaseOrderById")]
    [ProducesResponseType<PurchaseOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await purchaseOrderService.GetByIdAsync(id, cancellationToken);
        return purchaseOrder is null ? NotFound() : Ok(purchaseOrder);
    }

    [HttpPost]
    [ProducesResponseType<PurchaseOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseOrderResponse>> Create(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseOrderService.CreateAsync(request, cancellationToken);
        if (result.Status != PurchaseOrderOperationStatus.Success)
        {
            return OperationProblem(result);
        }

        var purchaseOrder = result.PurchaseOrder!;
        return CreatedAtAction(nameof(GetById), new { id = purchaseOrder.Id }, purchaseOrder);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<PurchaseOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseOrderResponse>> Update(
        int id,
        UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseOrderService.UpdateAsync(id, request, cancellationToken);
        return result.Status == PurchaseOrderOperationStatus.Success
            ? Ok(result.PurchaseOrder)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/issue")]
    [ProducesResponseType<PurchaseOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseOrderResponse>> Issue(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await purchaseOrderService.IssueAsync(id, cancellationToken);
        return result.Status == PurchaseOrderOperationStatus.Success
            ? Ok(result.PurchaseOrder)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType<PurchaseOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseOrderResponse>> Cancel(
        int id,
        CancelPurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseOrderService.CancelAsync(id, request, cancellationToken);
        return result.Status == PurchaseOrderOperationStatus.Success
            ? Ok(result.PurchaseOrder)
            : OperationProblem(result);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await purchaseOrderService.DeleteAsync(id, cancellationToken);
        return result.Status == PurchaseOrderOperationStatus.Success
            ? NoContent()
            : OperationProblem(result);
    }

    private ObjectResult OperationProblem(PurchaseOrderOperationResult result)
    {
        var (statusCode, title) = result.Status switch
        {
            PurchaseOrderOperationStatus.NotFound =>
                (StatusCodes.Status404NotFound, "Purchase order or related record was not found."),
            PurchaseOrderOperationStatus.InvalidState =>
                (StatusCodes.Status409Conflict, "Purchase order operation is not allowed."),
            PurchaseOrderOperationStatus.DuplicatePurchaseOrder =>
                (StatusCodes.Status409Conflict, "Purchase order already exists."),
            PurchaseOrderOperationStatus.QuotationNotSelected =>
                (StatusCodes.Status409Conflict, "Quotation has not been selected."),
            PurchaseOrderOperationStatus.SupplierUnavailable =>
                (StatusCodes.Status400BadRequest, "Supplier is unavailable."),
            PurchaseOrderOperationStatus.ValidationFailed =>
                (StatusCodes.Status400BadRequest, "Purchase order validation failed."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Purchase order operation failed.")
        };

        return StatusCode(statusCode, new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = result.ErrorMessage
        });
    }
}
