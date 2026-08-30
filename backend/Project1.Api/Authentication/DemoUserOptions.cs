namespace Project1.Api.Authentication;

public sealed class DemoUserOptions
{
    public const string SectionName = "DemoUsers";

    public bool Enabled { get; init; }

    public string DefaultPassword { get; init; } = string.Empty;

    public string DepartmentCode { get; init; } = "IT";
}
