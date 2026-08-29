using Microsoft.AspNetCore.Mvc;
using Project1.Api.DTOs.Products;
using Project1.Api.Services.Products;

namespace Project1.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return Ok(await productService.GetAllAsync(includeInactive, cancellationToken));
    }

    [HttpGet("{id:int}", Name = "GetProductById")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.CreateAsync(request, cancellationToken);

        if (result.Status != ProductSaveStatus.Success)
        {
            return RelatedRecordProblem(result.Status);
        }

        var product = result.Product!;
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Update(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await productService.UpdateAsync(id, request, cancellationToken);

        if (result.Status == ProductSaveStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status != ProductSaveStatus.Success)
        {
            return RelatedRecordProblem(result.Status);
        }

        return Ok(result.Product);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var result = await productService.DeactivateAsync(id, cancellationToken);
        return result.Status == ProductSaveStatus.NotFound ? NotFound() : NoContent();
    }

    private BadRequestObjectResult RelatedRecordProblem(ProductSaveStatus status)
    {
        var relation = status == ProductSaveStatus.ProductCategoryUnavailable
            ? "Product category"
            : "Unit of measure";

        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = $"{relation} is unavailable.",
            Detail = $"Select an active {relation.ToLowerInvariant()}."
        });
    }
}
