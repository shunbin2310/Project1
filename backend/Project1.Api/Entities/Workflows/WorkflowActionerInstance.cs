namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowActionerInstance
{
    public int Id { get; set; }

    public int ActionInstanceId { get; set; }

    public WorkflowActionInstance ActionInstance { get; set; } = null!;

    public WorkflowActionerType ActionerType { get; set; }

    public string ActionerKey { get; set; } = string.Empty;
}
