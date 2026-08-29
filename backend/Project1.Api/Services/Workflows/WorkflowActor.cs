namespace Project1.Api.Services.Workflows;

public sealed record WorkflowActor(
    string Name,
    IReadOnlyCollection<string> Roles);
