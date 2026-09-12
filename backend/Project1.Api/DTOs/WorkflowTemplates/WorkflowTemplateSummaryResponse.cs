namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed record WorkflowTemplateSummaryResponse(
    int Id,
    string Code,
    string Name,
    string EntityType,
    int Version,
    bool IsPublished,
    bool IsActive,
    int StepCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc);
