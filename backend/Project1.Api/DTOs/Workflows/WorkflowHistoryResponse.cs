namespace Project1.Api.DTOs.Workflows;

public sealed record WorkflowHistoryResponse(
    int Id,
    string? FromStepCode,
    string ToStepCode,
    string ActionCode,
    string ActionBy,
    string? Comment,
    DateTimeOffset ActionAtUtc);
