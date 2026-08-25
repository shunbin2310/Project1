using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Suppliers;
using Project1.Api.Services.Suppliers;

namespace Project1.Api.Tests.Controllers;

public sealed class SuppliersControllerTests
{
    [Fact]
    public void UpdateRequest_DoesNotExposeSupplierCode()
    {
        Assert.Null(typeof(UpdateSupplierRequest).GetProperty("Code"));
    }

    [Fact]
    public void CreateRequest_IsInvalid_WhenEmailFormatIsInvalid()
    {
        var request = new CreateSupplierRequest
        {
            Code = "SUP-001",
            Name = "Example Supplies",
            Email = "not-an-email"
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(
            validationResults,
            result => result.MemberNames.Contains(nameof(CreateSupplierRequest.Email)));
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenCodeAlreadyExists()
    {
        var service = new FakeSupplierService
        {
            CreateResult = new SupplierSaveResult(SupplierSaveStatus.DuplicateCode)
        };
        var controller = new SuppliersController(service);
        var request = new CreateSupplierRequest
        {
            Code = "SUP-001",
            Name = "Example Supplies"
        };

        var response = await controller.Create(request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("Supplier code already exists.", problem.Title);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenSupplierDoesNotExist()
    {
        var controller = new SuppliersController(new FakeSupplierService());

        var response = await controller.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task Deactivate_ReturnsNoContent_WhenSupplierExists()
    {
        var service = new FakeSupplierService
        {
            DeactivateResult = new SupplierSaveResult(
                SupplierSaveStatus.Success,
                CreateResponse(isActive: false))
        };
        var controller = new SuppliersController(service);

        var response = await controller.Deactivate(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    private static SupplierResponse CreateResponse(bool isActive = true) =>
        new(
            1,
            "SUP-001",
            "Example Supplies",
            null,
            null,
            null,
            null,
            isActive,
            DateTimeOffset.UtcNow,
            null);

    private sealed class FakeSupplierService : ISupplierService
    {
        public SupplierSaveResult CreateResult { get; init; } =
            new(SupplierSaveStatus.Success, CreateResponse());

        public SupplierSaveResult UpdateResult { get; init; } =
            new(SupplierSaveStatus.Success, CreateResponse());

        public SupplierSaveResult DeactivateResult { get; init; } =
            new(SupplierSaveStatus.NotFound);

        public Task<IReadOnlyList<SupplierResponse>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<SupplierResponse> suppliers = [];
            return Task.FromResult(suppliers);
        }

        public Task<SupplierResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<SupplierResponse?>(null);
        }

        public Task<SupplierSaveResult> CreateAsync(
            CreateSupplierRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResult);
        }

        public Task<SupplierSaveResult> UpdateAsync(
            int id,
            UpdateSupplierRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateResult);
        }

        public Task<SupplierSaveResult> DeactivateAsync(
            int id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeactivateResult);
        }
    }
}
