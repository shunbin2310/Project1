namespace Project1.Api.Entities.Workflows;

public sealed class WorkflowHistory
{
    public int Id { get; set; }

    public int ProcessInstanceId { get; set; }

    public WorkflowProcessInstance ProcessInstance { get; set; } = null!;

    public int? ActionInstanceId { get; set; }

    public string? FromStepCode { get; set; }

    public string ToStepCode { get; set; } = string.Empty;

    public string ActionCode { get; set; } = string.Empty;

    public string ActionBy { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public DateTimeOffset ActionAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
