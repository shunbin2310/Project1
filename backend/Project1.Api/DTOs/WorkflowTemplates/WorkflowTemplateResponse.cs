using Project1.Api.Entities.Workflows;

namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed record WorkflowTemplateResponse(
    int Id,
    string Code,
    string Name,
    string EntityType,
    int Version,
    bool IsPublished,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<WorkflowTemplateStepResponse> Steps);

public sealed record WorkflowTemplateStepResponse(
    int Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsInitial,
    bool IsTerminal,
    IReadOnlyList<WorkflowTemplateActionResponse> Actions);

public sealed record WorkflowTemplateActionResponse(
    int Id,
    string Code,
    string Name,
    string ToStepCode,
    bool RequiresComment,
    IReadOnlyList<WorkflowTemplateActionerResponse> Actioners);

public sealed record WorkflowTemplateActionerResponse(
    int Id,
    WorkflowActionerType ActionerType,
    string? ActionerKey);
