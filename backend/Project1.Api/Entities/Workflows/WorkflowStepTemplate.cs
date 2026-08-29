namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowStepTemplate
{
    public int Id { get; set; }

    public int ProcessTemplateId { get; set; }

    public WorkflowProcessTemplate ProcessTemplate { get; set; } = null!;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsInitial { get; set; }

    public bool IsTerminal { get; set; }

    public ICollection<WorkflowActionTemplate> Actions { get; set; } = [];
}
