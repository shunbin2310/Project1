using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.Controllers;
using Project1.Api.DTOs.EmailTemplates;
using Project1.Api.Services.EmailTemplates;

namespace Project1.Api.Tests.Controllers;

public sealed class EmailTemplatesControllerTests
{
    [Fact]
    public void Controller_RequiresAdminRole()
    {
        var attribute = Assert.Single(typeof(EmailTemplatesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(ApplicationRoles.Admin, attribute.Roles);
    }

    [Fact]
    public async Task Publish_ReturnsConflict_WhenTemplateIsNotDraft()
    {
        var service = new FakeEmailTemplateService
        {
            PublishResult = new EmailTemplateOperationResult(
                EmailTemplateOperationStatus.InvalidState,
                ErrorMessage: "Only a draft can be published.")
        };
        var controller = new EmailTemplatesController(service);

        var result = await controller.Publish(1, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal("Only a draft can be published.", problem.Detail);
    }

    private sealed class FakeEmailTemplateService : IEmailTemplateService
    {
        public EmailTemplateOperationResult PublishResult { get; init; } =
            new(EmailTemplateOperationStatus.NotFound);

        public Task<IReadOnlyList<EmailTemplateSummaryResponse>> GetAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmailTemplateSummaryResponse>>([]);

        public Task<EmailTemplateResponse?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<EmailTemplateResponse?>(null);

        public Task<EmailTemplateOperationResult> CreateVersionAsync(
            int sourceTemplateId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailTemplateOperationResult(EmailTemplateOperationStatus.NotFound));

        public Task<EmailTemplateOperationResult> UpdateAsync(
            int id,
            UpdateEmailTemplateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailTemplateOperationResult(EmailTemplateOperationStatus.NotFound));

        public Task<EmailTemplateOperationResult> PreviewAsync(
            PreviewEmailTemplateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailTemplateOperationResult(EmailTemplateOperationStatus.NotFound));

        public Task<EmailTemplateOperationResult> PublishAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult(PublishResult);

        public Task<EmailTemplateOperationResult> DeleteAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailTemplateOperationResult(EmailTemplateOperationStatus.NotFound));
    }
}
