using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.Suppliers;
using Project1.Api.Services.Suppliers;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class SuppliersController(ISupplierService supplierService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SupplierResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierResponse>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var suppliers = await supplierService.GetAllAsync(
            includeInactive,
            cancellationToken);

        return Ok(suppliers);
    }

    [HttpGet("{id:int}", Name = "GetSupplierById")]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);

        return supplier is null ? NotFound() : Ok(supplier);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPost]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupplierResponse>> Create(
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.CreateAsync(request, cancellationToken);

        var supplier = result.Supplier!;

        return CreatedAtAction(
            nameof(GetById),
            new { id = supplier.Id },
            supplier);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPut("{id:int}")]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierResponse>> Update(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.UpdateAsync(id, request, cancellationToken);

        if (result.Status == SupplierSaveStatus.NotFound)
        {
            return NotFound();
        }

        return Ok(result.Supplier);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await supplierService.DeactivateAsync(id, cancellationToken);

        return result.Status == SupplierSaveStatus.NotFound
            ? NotFound()
            : NoContent();
    }

}
