namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowActionTemplate
{
    public int Id { get; set; }

    public int FromStepTemplateId { get; set; }

    public WorkflowStepTemplate FromStepTemplate { get; set; } = null!;

    public int ToStepTemplateId { get; set; }

    public WorkflowStepTemplate ToStepTemplate { get; set; } = null!;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool RequiresComment { get; set; }

    public ICollection<WorkflowActionerTemplate> Actioners { get; set; } = [];
}
