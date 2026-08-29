using Microsoft.AspNetCore.Mvc;
using Project1.Api.Controllers;
using Project1.Api.DTOs.UnitsOfMeasure;
using Project1.Api.Services.UnitsOfMeasure;

namespace Project1.Api.Tests.Controllers;

public sealed class UnitsOfMeasureControllerTests
{
    [Fact]
    public void UpdateRequest_DoesNotExposeUnitCode()
    {
        Assert.Null(typeof(UpdateUnitOfMeasureRequest).GetProperty("Code"));
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenCodeAlreadyExists()
    {
        var service = new FakeUnitOfMeasureService
        {
            CreateResult = new UnitOfMeasureSaveResult(UnitOfMeasureSaveStatus.DuplicateCode)
        };
        var controller = new UnitsOfMeasureController(service);

        var response = await controller.Create(
            new CreateUnitOfMeasureRequest { Code = "UNIT", Name = "Unit" },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Unit of measure code already exists.", problem.Title);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenUnitDoesNotExist()
    {
        var controller = new UnitsOfMeasureController(new FakeUnitOfMeasureService());

        var response = await controller.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    private static UnitOfMeasureResponse CreateResponse() =>
        new(1, "UNIT", "Unit", null, true, DateTimeOffset.UtcNow, null);

    private sealed class FakeUnitOfMeasureService : IUnitOfMeasureService
    {
        public UnitOfMeasureSaveResult CreateResult { get; init; } =
            new(UnitOfMeasureSaveStatus.Success, CreateResponse());

        public UnitOfMeasureSaveResult UpdateResult { get; init; } =
            new(UnitOfMeasureSaveStatus.Success, CreateResponse());

        public UnitOfMeasureSaveResult DeactivateResult { get; init; } =
            new(UnitOfMeasureSaveStatus.NotFound);

        public Task<IReadOnlyList<UnitOfMeasureResponse>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UnitOfMeasureResponse>>([]);

        public Task<UnitOfMeasureResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<UnitOfMeasureResponse?>(null);

        public Task<UnitOfMeasureSaveResult> CreateAsync(
            CreateUnitOfMeasureRequest request,
            CancellationToken cancellationToken) => Task.FromResult(CreateResult);

        public Task<UnitOfMeasureSaveResult> UpdateAsync(
            int id,
            UpdateUnitOfMeasureRequest request,
            CancellationToken cancellationToken) => Task.FromResult(UpdateResult);

        public Task<UnitOfMeasureSaveResult> DeactivateAsync(
            int id,
            CancellationToken cancellationToken) => Task.FromResult(DeactivateResult);
    }
}
