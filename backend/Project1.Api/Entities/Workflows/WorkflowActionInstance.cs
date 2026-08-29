namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowActionInstance
{
    public int Id { get; set; }

    public int FromStepInstanceId { get; set; }

    public WorkflowStepInstance FromStepInstance { get; set; } = null!;

    public int ToStepInstanceId { get; set; }

    public WorkflowStepInstance ToStepInstance { get; set; } = null!;

    public int SourceActionTemplateId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool RequiresComment { get; set; }

    public ICollection<WorkflowActionerInstance> Actioners { get; set; } = [];
}
