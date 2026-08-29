namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowActionerTemplate
{
    public int Id { get; set; }

    public int ActionTemplateId { get; set; }

    public WorkflowActionTemplate ActionTemplate { get; set; } = null!;

    public WorkflowActionerType ActionerType { get; set; }

    public string? ActionerKey { get; set; }
}
