using Project1.Api.Entities.Workflows;

namespace Project1.Api.DTOs.Workflows;

public sealed record WorkflowInstanceResponse(
    int Id,
    string TemplateCode,
    string TemplateName,
    int TemplateVersion,
    string EntityType,
    int EntityId,
    WorkflowInstanceStatus Status,
    string CurrentStepCode,
    string CurrentStepName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<WorkflowAvailableActionResponse> AvailableActions,
    IReadOnlyList<WorkflowHistoryResponse> History);
