namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowStepInstance
{
    public int Id { get; set; }

    public int ProcessInstanceId { get; set; }

    public WorkflowProcessInstance ProcessInstance { get; set; } = null!;

    public int SourceStepTemplateId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsInitial { get; set; }

    public bool IsTerminal { get; set; }

    public ICollection<WorkflowActionInstance> Actions { get; set; } = [];
}
