namespace Project1.Api.Services.Authentication;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    int UserId { get; }

    string DisplayName { get; }

    int? DepartmentId { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}
