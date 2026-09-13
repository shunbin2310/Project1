using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.Quotations;
using Project1.Api.Entities;
using Project1.Api.Services.Quotations;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/quotations")]
public sealed class QuotationsController(IQuotationService quotationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<QuotationResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<QuotationResponse>>> GetAll(
        [FromQuery] int? purchaseRequestId = null,
        [FromQuery] QuotationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var quotations = await quotationService.GetAllAsync(
            purchaseRequestId,
            status,
            cancellationToken);

        return Ok(quotations);
    }

    [HttpGet("comparison")]
    [ProducesResponseType<QuotationComparisonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationComparisonResponse>> GetComparison(
        [FromQuery] int purchaseRequestId,
        CancellationToken cancellationToken)
    {
        var comparison = await quotationService.GetComparisonAsync(
            purchaseRequestId,
            cancellationToken);

        return comparison is null ? NotFound() : Ok(comparison);
    }

    [HttpGet("{id:int}", Name = "GetQuotationById")]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var quotation = await quotationService.GetByIdAsync(id, cancellationToken);
        return quotation is null ? NotFound() : Ok(quotation);
    }

    [HttpPost]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuotationResponse>> Create(
        CreateQuotationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await quotationService.CreateAsync(request, cancellationToken);
        if (result.Status != QuotationOperationStatus.Success)
        {
            return OperationProblem(result);
        }

        var quotation = result.Quotation!;
        return CreatedAtAction(nameof(GetById), new { id = quotation.Id }, quotation);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuotationResponse>> Update(
        int id,
        UpdateQuotationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await quotationService.UpdateAsync(id, request, cancellationToken);
        return result.Status == QuotationOperationStatus.Success
            ? Ok(result.Quotation)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/submit")]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuotationResponse>> Submit(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await quotationService.SubmitAsync(id, cancellationToken);
        return result.Status == QuotationOperationStatus.Success
            ? Ok(result.Quotation)
            : OperationProblem(result);
    }

    [HttpPost("{id:int}/select")]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuotationResponse>> Select(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await quotationService.SelectAsync(id, cancellationToken);
        return result.Status == QuotationOperationStatus.Success
            ? Ok(result.Quotation)
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
        var result = await quotationService.DeleteAsync(id, cancellationToken);
        return result.Status == QuotationOperationStatus.Success
            ? NoContent()
            : OperationProblem(result);
    }

    private ObjectResult OperationProblem(QuotationOperationResult result)
    {
        var (statusCode, title) = result.Status switch
        {
            QuotationOperationStatus.NotFound =>
                (StatusCodes.Status404NotFound, "Quotation or related record was not found."),
            QuotationOperationStatus.InvalidState =>
                (StatusCodes.Status409Conflict, "Quotation operation is not allowed."),
            QuotationOperationStatus.DuplicateQuotation =>
                (StatusCodes.Status409Conflict, "Quotation already exists."),
            QuotationOperationStatus.SupplierUnavailable =>
                (StatusCodes.Status400BadRequest, "Supplier is unavailable."),
            QuotationOperationStatus.SupplierCannotSupplyProducts =>
                (StatusCodes.Status400BadRequest, "Supplier cannot supply this request."),
            QuotationOperationStatus.ValidationFailed =>
                (StatusCodes.Status400BadRequest, "Quotation validation failed."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Quotation operation failed.")
        };

        return StatusCode(statusCode, new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = result.ErrorMessage
        });
    }
}
