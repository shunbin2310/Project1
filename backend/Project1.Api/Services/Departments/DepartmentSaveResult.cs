using Project1.Api.DTOs.Departments;

namespace Project1.Api.Services.Departments;

public enum DepartmentSaveStatus
{
    Success,
    NotFound,
    DuplicateCode
}

public sealed record DepartmentSaveResult(
    DepartmentSaveStatus Status,
    DepartmentResponse? Department = null);
