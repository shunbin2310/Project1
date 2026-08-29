using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.ProductCategories;
using Project1.Api.Services.ProductCategories;

namespace Project1.Api.Tests.Controllers;

public sealed class ProductCategoriesControllerTests
{
    [Fact]
    public void CreateAndUpdateRequests_DoNotExposeCategoryCode()
    {
        Assert.Null(typeof(CreateProductCategoryRequest).GetProperty("Code"));
        Assert.Null(typeof(UpdateProductCategoryRequest).GetProperty("Code"));
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenNameAlreadyExists()
    {
        var service = new FakeProductCategoryService
        {
            CreateResult = new ProductCategorySaveResult(ProductCategorySaveStatus.DuplicateName)
        };
        var controller = new ProductCategoriesController(service);

        var response = await controller.Create(
            new CreateProductCategoryRequest { Name = "Electronics" },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Product category name already exists.", problem.Title);
    }

    [Fact]
    public async Task Deactivate_ReturnsNoContent_WhenCategoryExists()
    {
        var service = new FakeProductCategoryService
        {
            DeactivateResult = new ProductCategorySaveResult(
                ProductCategorySaveStatus.Success,
                CreateResponse(isActive: false))
        };
        var controller = new ProductCategoriesController(service);

        var response = await controller.Deactivate(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    private static ProductCategoryResponse CreateResponse(bool isActive = true) =>
        new(1, "CAT-0001", "Electronics", null, isActive, DateTimeOffset.UtcNow, null);

    private sealed class FakeProductCategoryService : IProductCategoryService
    {
        public ProductCategorySaveResult CreateResult { get; init; } =
            new(ProductCategorySaveStatus.Success, CreateResponse());

        public ProductCategorySaveResult UpdateResult { get; init; } =
            new(ProductCategorySaveStatus.Success, CreateResponse());

        public ProductCategorySaveResult DeactivateResult { get; init; } =
            new(ProductCategorySaveStatus.NotFound);

        public Task<IReadOnlyList<ProductCategoryResponse>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCategoryResponse>>([]);

        public Task<ProductCategoryResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProductCategoryResponse?>(null);

        public Task<ProductCategorySaveResult> CreateAsync(
            CreateProductCategoryRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<ProductCategorySaveResult> UpdateAsync(
            int id,
            UpdateProductCategoryRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<ProductCategorySaveResult> DeactivateAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeactivateResult);
    }
}
