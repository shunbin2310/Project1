using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Products;
using Project1.Api.Services.Products;

namespace Project1.Api.Tests.Controllers;

public sealed class ProductsControllerTests
{
    [Fact]
    public void CreateRequest_IsInvalid_WhenPriceOrReorderLevelIsNegative()
    {
        var request = new CreateProductRequest
        {
            Name = "Dell Monitor",
            ProductCategoryId = 1,
            UnitOfMeasureId = 1,
            DefaultUnitPrice = -1,
            ReorderLevel = -1
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(CreateProductRequest.DefaultUnitPrice)));
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(CreateProductRequest.ReorderLevel)));
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenCategoryIsUnavailable()
    {
        var service = new FakeProductService
        {
            CreateResult = new ProductSaveResult(ProductSaveStatus.ProductCategoryUnavailable)
        };
        var controller = new ProductsController(service);

        var response = await controller.Create(CreateRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Product category is unavailable.", problem.Title);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenProductDoesNotExist()
    {
        var controller = new ProductsController(new FakeProductService());
        var request = new UpdateProductRequest
        {
            Name = "Dell Monitor",
            ProductCategoryId = 1,
            UnitOfMeasureId = 1,
            IsActive = true
        };

        var response = await controller.Update(999, request, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    private static CreateProductRequest CreateRequest() =>
        new()
        {
            Name = "Dell Monitor",
            ProductCategoryId = 1,
            UnitOfMeasureId = 1,
            DefaultUnitPrice = 1299.90m,
            ReorderLevel = 5m
        };

    private static ProductResponse CreateResponse() =>
        new(
            1,
            "ITEM-0001",
            "Dell Monitor",
            null,
            1,
            "CAT-0001",
            "Electronics",
            1,
            "UNIT",
            "Unit",
            1299.90m,
            5m,
            true,
            DateTimeOffset.UtcNow,
            null);

    private sealed class FakeProductService : IProductService
    {
        public ProductSaveResult CreateResult { get; init; } =
            new(ProductSaveStatus.Success, CreateResponse());

        public ProductSaveResult UpdateResult { get; init; } =
            new(ProductSaveStatus.NotFound);

        public ProductSaveResult DeactivateResult { get; init; } =
            new(ProductSaveStatus.NotFound);

        public Task<IReadOnlyList<ProductResponse>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductResponse>>([]);

        public Task<ProductResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProductResponse?>(null);

        public Task<ProductSaveResult> CreateAsync(
            CreateProductRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<ProductSaveResult> UpdateAsync(
            int id,
            UpdateProductRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<ProductSaveResult> DeactivateAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeactivateResult);
    }
}
