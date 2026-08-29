using Project1.Api.Entities.Workflows;

namespace Project1.Api.DTOs.Workflows;

public sealed record WorkflowActionerResponse(
    WorkflowActionerType ActionerType,
    string ActionerKey);
