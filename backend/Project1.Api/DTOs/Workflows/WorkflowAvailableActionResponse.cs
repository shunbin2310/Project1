namespace Project1.Api.DTOs.Workflows;

public sealed record WorkflowAvailableActionResponse(
    string Code,
    string Name,
    bool RequiresComment,
    string ToStepCode,
    string ToStepName,
    IReadOnlyList<WorkflowActionerResponse> Actioners);
