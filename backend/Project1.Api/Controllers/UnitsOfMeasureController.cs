using Microsoft.AspNetCore.Mvc;
using Project1.Api.DTOs.UnitsOfMeasure;
using Project1.Api.Services.UnitsOfMeasure;

namespace Project1.Api.Controllers;

[ApiController]
[Route("api/units-of-measure")]
public sealed class UnitsOfMeasureController(IUnitOfMeasureService unitService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UnitOfMeasureResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UnitOfMeasureResponse>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return Ok(await unitService.GetAllAsync(includeInactive, cancellationToken));
    }

    [HttpGet("{id:int}", Name = "GetUnitOfMeasureById")]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UnitOfMeasureResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var unit = await unitService.GetByIdAsync(id, cancellationToken);
        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPost]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Create(
        CreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var result = await unitService.CreateAsync(request, cancellationToken);

        if (result.Status == UnitOfMeasureSaveStatus.DuplicateCode)
        {
            return UnitCodeConflict(request.Code);
        }

        var unit = result.UnitOfMeasure!;
        return CreatedAtAction(nameof(GetById), new { id = unit.Id }, unit);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<UnitOfMeasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UnitOfMeasureResponse>> Update(
        int id,
        UpdateUnitOfMeasureRequest request,
        CancellationToken cancellationToken)
    {
        var result = await unitService.UpdateAsync(id, request, cancellationToken);
        return result.Status == UnitOfMeasureSaveStatus.NotFound
            ? NotFound()
            : Ok(result.UnitOfMeasure);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var result = await unitService.DeactivateAsync(id, cancellationToken);
        return result.Status == UnitOfMeasureSaveStatus.NotFound ? NotFound() : NoContent();
    }

    private ConflictObjectResult UnitCodeConflict(string code)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Unit of measure code already exists.",
            Detail = $"A unit of measure with code '{code.Trim().ToUpperInvariant()}' already exists."
        });
    }
}
