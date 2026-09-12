using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.Controllers;
using Project1.Api.DTOs.WorkflowTemplates;
using Project1.Api.Services.WorkflowTemplates;

namespace Project1.Api.Tests.Controllers;

public sealed class WorkflowTemplatesControllerTests
{
    [Fact]
    public void Controller_RequiresAdminRole()
    {
        var attribute = Assert.Single(typeof(WorkflowTemplatesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(ApplicationRoles.Admin, attribute.Roles);
    }

    [Fact]
    public async Task Publish_ReturnsConflict_WhenTemplateIsAlreadyPublished()
    {
        var service = new FakeWorkflowTemplateService
        {
            PublishResult = new WorkflowTemplateOperationResult(
                WorkflowTemplateOperationStatus.InvalidState,
                ErrorMessage: "Already published.")
        };
        var controller = new WorkflowTemplatesController(service);

        var result = await controller.Publish(1, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal("Already published.", problem.Detail);
    }

    private sealed class FakeWorkflowTemplateService : IWorkflowTemplateService
    {
        public WorkflowTemplateOperationResult PublishResult { get; init; } =
            new(WorkflowTemplateOperationStatus.NotFound);

        public Task<IReadOnlyList<WorkflowTemplateSummaryResponse>> GetAllAsync(
            string? code,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkflowTemplateSummaryResponse>>([]);

        public Task<WorkflowTemplateResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<WorkflowTemplateResponse?>(null);

        public Task<WorkflowTemplateOperationResult> CreateAsync(
            CreateWorkflowTemplateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowTemplateOperationResult(
                WorkflowTemplateOperationStatus.NotFound));

        public Task<WorkflowTemplateOperationResult> CreateVersionAsync(
            int sourceTemplateId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowTemplateOperationResult(
                WorkflowTemplateOperationStatus.NotFound));

        public Task<WorkflowTemplateOperationResult> UpdateAsync(
            int id,
            UpdateWorkflowTemplateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowTemplateOperationResult(
                WorkflowTemplateOperationStatus.NotFound));

        public Task<WorkflowTemplateOperationResult> PublishAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult(PublishResult);

        public Task<WorkflowTemplateOperationResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowTemplateOperationResult(
                WorkflowTemplateOperationStatus.NotFound));
    }
}
