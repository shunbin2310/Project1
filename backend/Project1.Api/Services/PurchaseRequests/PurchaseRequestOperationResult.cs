using Project1.Api.DTOs.PurchaseRequests;

namespace Project1.Api.Services.PurchaseRequests;

public enum PurchaseRequestOperationStatus
{
    Success,
    NotFound,
    InvalidState,
    DepartmentNotFound,
    ProductNotFound,
    ValidationFailed,
    WorkflowUnavailable,
    Unauthorized
}

public sealed record PurchaseRequestOperationResult(
    PurchaseRequestOperationStatus Status,
    PurchaseRequestResponse? PurchaseRequest = null,
    string? ErrorMessage = null);
