using Microsoft.AspNetCore.Mvc;
using Project1.Api.DTOs.Departments;
using Project1.Api.Services.Departments;

namespace Project1.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DepartmentsController(IDepartmentService departmentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DepartmentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DepartmentResponse>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var departments = await departmentService.GetAllAsync(
            includeInactive,
            cancellationToken);

        return Ok(departments);
    }

    [HttpGet("{id:int}", Name = nameof(GetById))]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.GetByIdAsync(id, cancellationToken);

        return department is null ? NotFound() : Ok(department);
    }

    [HttpPost]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentResponse>> Create(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.CreateAsync(request, cancellationToken);

        if (result.Status == DepartmentSaveStatus.DuplicateCode)
        {
            return DepartmentCodeConflict(request.Code);
        }

        var department = result.Department!;

        return CreatedAtAction(
            nameof(GetById),
            new { id = department.Id },
            department);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentResponse>> Update(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.UpdateAsync(id, request, cancellationToken);

        if (result.Status == DepartmentSaveStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status == DepartmentSaveStatus.DuplicateCode)
        {
            return DepartmentCodeConflict(request.Code);
        }

        return Ok(result.Department);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await departmentService.DeactivateAsync(id, cancellationToken);

        return result.Status == DepartmentSaveStatus.NotFound
            ? NotFound()
            : NoContent();
    }

    private ConflictObjectResult DepartmentCodeConflict(string code)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Department code already exists.",
            Detail = $"A department with code '{code.Trim().ToUpperInvariant()}' already exists."
        });
    }
}
