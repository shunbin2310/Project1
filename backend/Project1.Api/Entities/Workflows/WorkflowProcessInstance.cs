namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowProcessInstance
{
    public int Id { get; set; }

    public int ProcessTemplateId { get; set; }

    public WorkflowProcessTemplate ProcessTemplate { get; set; } = null!;

    public string TemplateCode { get; set; } = string.Empty;

    public string TemplateName { get; set; } = string.Empty;

    public int TemplateVersion { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public int CurrentStepInstanceId { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;

    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ICollection<WorkflowStepInstance> Steps { get; set; } = [];

    public ICollection<WorkflowHistory> History { get; set; } = [];
}
