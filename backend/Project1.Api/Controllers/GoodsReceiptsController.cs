using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.GoodsReceipts;
using Project1.Api.Entities;
using Project1.Api.Services.GoodsReceipts;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/goods-receipts")]
public sealed class GoodsReceiptsController(
    IGoodsReceiptService goodsReceiptService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GoodsReceiptResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GoodsReceiptResponse>>> GetAll(
        [FromQuery] int? purchaseOrderId = null,
        [FromQuery] int? supplierId = null,
        [FromQuery] GoodsReceiptStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var receipts = await goodsReceiptService.GetAllAsync(
            purchaseOrderId,
            supplierId,
            status,
            cancellationToken);

        return Ok(receipts);
    }

    [HttpGet("{id:int}", Name = "GetGoodsReceiptById")]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GoodsReceiptResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var receipt = await goodsReceiptService.GetByIdAsync(id, cancellationToken);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpPost]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GoodsReceiptResponse>> Create(
        CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var result = await goodsReceiptService.CreateAsync(request, cancellationToken);
        if (result.Status != GoodsReceiptOperationStatus.Success)
        {
            return OperationProblem(result);
        }

        var receipt = result.GoodsReceipt!;
        return CreatedAtAction(nameof(GetById), new { id = receipt.Id }, receipt);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GoodsReceiptResponse>> Update(
        int id,
        UpdateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var result = await goodsReceiptService.UpdateAsync(id, request, cancellationToken);
        return result.Status == GoodsReceiptOperationStatus.Success
            ? Ok(result.GoodsReceipt)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/post")]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GoodsReceiptResponse>> Post(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await goodsReceiptService.PostAsync(id, cancellationToken);
        return result.Status == GoodsReceiptOperationStatus.Success
            ? Ok(result.GoodsReceipt)
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
        var result = await goodsReceiptService.DeleteAsync(id, cancellationToken);
        return result.Status == GoodsReceiptOperationStatus.Success
            ? NoContent()
            : OperationProblem(result);
    }

    private ObjectResult OperationProblem(GoodsReceiptOperationResult result)
    {
        var (statusCode, title) = result.Status switch
        {
            GoodsReceiptOperationStatus.NotFound =>
                (StatusCodes.Status404NotFound, "Goods receipt or related record was not found."),
            GoodsReceiptOperationStatus.ValidationFailed =>
                (StatusCodes.Status400BadRequest, "Goods receipt validation failed."),
            GoodsReceiptOperationStatus.InvalidState =>
                (StatusCodes.Status409Conflict, "Goods receipt operation is not allowed."),
            GoodsReceiptOperationStatus.DuplicateDraft =>
                (StatusCodes.Status409Conflict, "A draft goods receipt already exists."),
            GoodsReceiptOperationStatus.DuplicateDeliveryNote =>
                (StatusCodes.Status409Conflict, "Supplier delivery note already exists."),
            GoodsReceiptOperationStatus.QuantityExceeded =>
                (StatusCodes.Status409Conflict, "Received quantity exceeds the purchase order."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Goods receipt operation failed.")
        };

        return StatusCode(statusCode, new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = result.ErrorMessage
        });
    }
}
