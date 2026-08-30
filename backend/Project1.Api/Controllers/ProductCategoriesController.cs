using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.ProductCategories;
using Project1.Api.Services.ProductCategories;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/product-categories")]
public sealed class ProductCategoriesController(IProductCategoryService categoryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProductCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductCategoryResponse>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return Ok(await categoryService.GetAllAsync(includeInactive, cancellationToken));
    }

    [HttpGet("{id:int}", Name = "GetProductCategoryById")]
    [ProducesResponseType<ProductCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductCategoryResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : Ok(category);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPost]
    [ProducesResponseType<ProductCategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductCategoryResponse>> Create(
        CreateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await categoryService.CreateAsync(request, cancellationToken);

        if (result.Status == ProductCategorySaveStatus.DuplicateName)
        {
            return CategoryNameConflict(request.Name);
        }

        var category = result.ProductCategory!;
        return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPut("{id:int}")]
    [ProducesResponseType<ProductCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductCategoryResponse>> Update(
        int id,
        UpdateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await categoryService.UpdateAsync(id, request, cancellationToken);

        return result.Status switch
        {
            ProductCategorySaveStatus.NotFound => NotFound(),
            ProductCategorySaveStatus.DuplicateName => CategoryNameConflict(request.Name),
            _ => Ok(result.ProductCategory)
        };
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var result = await categoryService.DeactivateAsync(id, cancellationToken);
        return result.Status == ProductCategorySaveStatus.NotFound ? NotFound() : NoContent();
    }

    private ConflictObjectResult CategoryNameConflict(string name)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Product category name already exists.",
            Detail = $"A product category with name '{name.Trim()}' already exists."
        });
    }
}
