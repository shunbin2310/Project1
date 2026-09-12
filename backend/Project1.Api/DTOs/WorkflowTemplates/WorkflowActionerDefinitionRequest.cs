using Project1.Api.Entities.Workflows;

namespace Project1.Api.DTOs.WorkflowTemplates;

public sealed class WorkflowActionerDefinitionRequest
{
    public WorkflowActionerType ActionerType { get; init; }

    public string? ActionerKey { get; init; }
}
