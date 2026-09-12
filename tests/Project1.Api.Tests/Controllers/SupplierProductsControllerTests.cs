using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.SupplierProducts;
using Project1.Api.Services.SupplierProducts;

namespace Project1.Api.Tests.Controllers;

public sealed class SupplierProductsControllerTests
{
    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenRelationshipIsCreated()
    {
        var controller = new SupplierProductsController(new FakeSupplierProductService());

        var response = await controller.Create(
            new CreateSupplierProductRequest
            {
                SupplierId = 1,
                ProductId = 1,
                IsPreferred = true
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var supplierProduct = Assert.IsType<SupplierProductResponse>(created.Value);
        Assert.Equal("SUP-0001", supplierProduct.SupplierCode);
        Assert.Equal("ITEM-0001", supplierProduct.ProductCode);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenRelationshipAlreadyExists()
    {
        var service = new FakeSupplierProductService
        {
            CreateResult = new SupplierProductSaveResult(
                SupplierProductSaveStatus.DuplicateRelationship)
        };
        var controller = new SupplierProductsController(service);

        var response = await controller.Create(
            new CreateSupplierProductRequest { SupplierId = 1, ProductId = 1 },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Supplier-product relationship already exists.", problem.Title);
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_WhenProductIsUnavailable()
    {
        var service = new FakeSupplierProductService
        {
            UpdateResult = new SupplierProductSaveResult(
                SupplierProductSaveStatus.ProductUnavailable)
        };
        var controller = new SupplierProductsController(service);

        var response = await controller.Update(
            1,
            new UpdateSupplierProductRequest { IsActive = true },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Product is unavailable.", problem.Title);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenRelationshipDoesNotExist()
    {
        var service = new FakeSupplierProductService
        {
            DeleteResult = new SupplierProductSaveResult(
                SupplierProductSaveStatus.NotFound)
        };
        var controller = new SupplierProductsController(service);

        var response = await controller.Delete(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response);
    }

    private static SupplierProductResponse CreateResponse() =>
        new(
            1,
            1,
            "SUP-0001",
            "Example Supplies",
            1,
            "ITEM-0001",
            "Dell Monitor",
            "UNIT",
            "Unit",
            1399.90m,
            true,
            true,
            DateTimeOffset.UtcNow,
            null);

    private sealed class FakeSupplierProductService : ISupplierProductService
    {
        public SupplierProductSaveResult CreateResult { get; init; } =
            new(SupplierProductSaveStatus.Success, CreateResponse());

        public SupplierProductSaveResult UpdateResult { get; init; } =
            new(SupplierProductSaveStatus.Success, CreateResponse());

        public SupplierProductSaveResult DeleteResult { get; init; } =
            new(SupplierProductSaveStatus.Success);

        public Task<IReadOnlyList<SupplierProductResponse>> GetAllAsync(
            int? supplierId,
            int? productId,
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SupplierProductResponse>>([]);

        public Task<SupplierProductResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<SupplierProductResponse?>(null);

        public Task<SupplierProductSaveResult> CreateAsync(
            CreateSupplierProductRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<SupplierProductSaveResult> UpdateAsync(
            int id,
            UpdateSupplierProductRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<SupplierProductSaveResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeleteResult);
    }
}
