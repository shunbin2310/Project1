namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowProcessTemplate
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public int Version { get; set; }

    public bool IsPublished { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public ICollection<WorkflowStepTemplate> Steps { get; set; } = [];
}
