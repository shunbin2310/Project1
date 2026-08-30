namespace Project1.Api.Services.Workflows;

public sealed record WorkflowActor(
    int UserId,
    string Name,
    IReadOnlyCollection<string> Roles);
