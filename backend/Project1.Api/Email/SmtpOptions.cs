namespace Project1.Api.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseSsl { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "purchasing@project1.local";

    public string FromName { get; set; } = "Project1 Purchasing";

    public int PollIntervalSeconds { get; set; } = 5;
}
