using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.SupplierProducts;
using Project1.Api.Services.SupplierProducts;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/supplier-products")]
public sealed class SupplierProductsController(
    ISupplierProductService supplierProductService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SupplierProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierProductResponse>>> GetAll(
        [FromQuery] int? supplierId = null,
        [FromQuery] int? productId = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var supplierProducts = await supplierProductService.GetAllAsync(
            supplierId,
            productId,
            includeInactive,
            cancellationToken);

        return Ok(supplierProducts);
    }

    [HttpGet("{id:int}", Name = "GetSupplierProductById")]
    [ProducesResponseType<SupplierProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierProductResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var supplierProduct = await supplierProductService.GetByIdAsync(
            id,
            cancellationToken);

        return supplierProduct is null ? NotFound() : Ok(supplierProduct);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPost]
    [ProducesResponseType<SupplierProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierProductResponse>> Create(
        CreateSupplierProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierProductService.CreateAsync(request, cancellationToken);

        if (result.Status != SupplierProductSaveStatus.Success)
        {
            return SaveProblem(result.Status);
        }

        var supplierProduct = result.SupplierProduct!;
        return CreatedAtAction(
            nameof(GetById),
            new { id = supplierProduct.Id },
            supplierProduct);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPut("{id:int}")]
    [ProducesResponseType<SupplierProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierProductResponse>> Update(
        int id,
        UpdateSupplierProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await supplierProductService.UpdateAsync(
            id,
            request,
            cancellationToken);

        if (result.Status == SupplierProductSaveStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status != SupplierProductSaveStatus.Success)
        {
            return SaveProblem(result.Status);
        }

        return Ok(result.SupplierProduct);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await supplierProductService.DeleteAsync(id, cancellationToken);

        return result.Status == SupplierProductSaveStatus.NotFound
            ? NotFound()
            : NoContent();
    }

    private ObjectResult SaveProblem(SupplierProductSaveStatus status)
    {
        return status switch
        {
            SupplierProductSaveStatus.SupplierUnavailable => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Supplier is unavailable.",
                Detail = "Select an active supplier."
            }),
            SupplierProductSaveStatus.ProductUnavailable => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Product is unavailable.",
                Detail = "Select an active product."
            }),
            SupplierProductSaveStatus.DuplicateRelationship => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier-product relationship already exists.",
                Detail = "The selected supplier is already linked to this product."
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Supplier-product relationship could not be saved."
            })
        };
    }
}
