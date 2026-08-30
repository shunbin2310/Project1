namespace Project1.Api.Authentication;

public static class ApplicationRoles
{
    public const string Requester = "REQUESTER";
    public const string DepartmentApprover = "DEPARTMENT_APPROVER";
    public const string FinanceApprover = "FINANCE_APPROVER";
    public const string Admin = "ADMIN";

    public static readonly string[] All =
    [
        Requester,
        DepartmentApprover,
        FinanceApprover,
        Admin
    ];
}
