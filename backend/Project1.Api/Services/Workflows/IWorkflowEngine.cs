using Project1.Api.DTOs.Workflows;

namespace Project1.Api.Services.Workflows;

public interface IWorkflowEngine
{
    Task<WorkflowExecutionResult> StartAsync(
        string entityType,
        int entityId,
        string? requesterName,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionResult> ExecuteActionAsync(
        string entityType,
        int entityId,
        string actionCode,
        WorkflowActor actor,
        string? comment,
        CancellationToken cancellationToken);

    Task<WorkflowInstanceResponse?> GetInstanceAsync(
        string entityType,
        int entityId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, WorkflowInstanceResponse>> GetInstancesAsync(
        string entityType,
        IReadOnlyCollection<int> entityIds,
        CancellationToken cancellationToken);

    Task UpdateRequesterAsync(
        string entityType,
        int entityId,
        string? requesterName,
        CancellationToken cancellationToken);

    Task<bool> DeleteInstanceAsync(
        string entityType,
        int entityId,
        CancellationToken cancellationToken);
}
