using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Departments;
using Project1.Api.Services.Departments;

namespace Project1.Api.Tests.Controllers;

public sealed class DepartmentsControllerTests
{
    [Fact]
    public void UpdateRequest_DoesNotExposeDepartmentCode()
    {
        Assert.Null(typeof(UpdateDepartmentRequest).GetProperty("Code"));
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenCodeAlreadyExists()
    {
        var service = new FakeDepartmentService
        {
            CreateResult = new DepartmentSaveResult(DepartmentSaveStatus.DuplicateCode)
        };
        var controller = new DepartmentsController(service);
        var request = new CreateDepartmentRequest
        {
            Code = "IT",
            Name = "Information Technology"
        };

        var response = await controller.Create(request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("Department code already exists.", problem.Title);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var controller = new DepartmentsController(new FakeDepartmentService());

        var response = await controller.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task Deactivate_ReturnsNoContent_WhenDepartmentExists()
    {
        var service = new FakeDepartmentService
        {
            DeactivateResult = new DepartmentSaveResult(
                DepartmentSaveStatus.Success,
                CreateResponse(isActive: false))
        };
        var controller = new DepartmentsController(service);

        var response = await controller.Deactivate(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    private static DepartmentResponse CreateResponse(bool isActive = true) =>
        new(
            1,
            "IT",
            "Information Technology",
            null,
            isActive,
            DateTimeOffset.UtcNow,
            null);

    private sealed class FakeDepartmentService : IDepartmentService
    {
        public DepartmentSaveResult CreateResult { get; init; } =
            new(DepartmentSaveStatus.Success, CreateResponse());

        public DepartmentSaveResult UpdateResult { get; init; } =
            new(DepartmentSaveStatus.Success, CreateResponse());

        public DepartmentSaveResult DeactivateResult { get; init; } =
            new(DepartmentSaveStatus.NotFound);

        public Task<IReadOnlyList<DepartmentResponse>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DepartmentResponse> departments = [];
            return Task.FromResult(departments);
        }

        public Task<DepartmentResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<DepartmentResponse?>(null);
        }

        public Task<DepartmentSaveResult> CreateAsync(
            CreateDepartmentRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResult);
        }

        public Task<DepartmentSaveResult> UpdateAsync(
            int id,
            UpdateDepartmentRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateResult);
        }

        public Task<DepartmentSaveResult> DeactivateAsync(
            int id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeactivateResult);
        }
    }
}
