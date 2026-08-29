using Project1.Api.DTOs.Workflows;

namespace Project1.Api.DTOs.PurchaseRequests;

public sealed record PurchaseRequestResponse(
    int Id,
    string RequestNumber,
    string? RequesterName,
    int? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    DateOnly? RequiredDate,
    string? Justification,
    decimal EstimatedTotal,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<PurchaseRequestItemResponse> Items,
    WorkflowInstanceResponse Workflow);
