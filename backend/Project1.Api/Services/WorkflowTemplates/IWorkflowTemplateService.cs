using Project1.Api.DTOs.WorkflowTemplates;

namespace Project1.Api.Services.WorkflowTemplates;

public interface IWorkflowTemplateService
{
    Task<IReadOnlyList<WorkflowTemplateSummaryResponse>> GetAllAsync(
        string? code,
        CancellationToken cancellationToken);

    Task<WorkflowTemplateResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<WorkflowTemplateOperationResult> CreateAsync(
        CreateWorkflowTemplateRequest request,
        CancellationToken cancellationToken);

    Task<WorkflowTemplateOperationResult> CreateVersionAsync(
        int sourceTemplateId,
        CancellationToken cancellationToken);

    Task<WorkflowTemplateOperationResult> UpdateAsync(
        int id,
        UpdateWorkflowTemplateRequest request,
        CancellationToken cancellationToken);

    Task<WorkflowTemplateOperationResult> PublishAsync(
        int id,
        CancellationToken cancellationToken);

    Task<WorkflowTemplateOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken);
}
