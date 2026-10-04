namespace Project1.Api.DTOs.Dashboard;

public sealed record DashboardResponse(
    IReadOnlyList<DashboardSummaryCardResponse> SummaryCards,
    IReadOnlyList<DashboardReminderResponse> Reminders,
    IReadOnlyList<DashboardActivityResponse> RecentActivity,
    DateTimeOffset GeneratedAtUtc);

public sealed record DashboardSummaryCardResponse(
    string Key,
    string Label,
    int Value,
    string Description,
    string Route,
    string Tone);

public sealed record DashboardReminderResponse(
    string Key,
    string Title,
    string Description,
    int Count,
    string Route,
    string Severity);

public sealed record DashboardActivityResponse(
    string Key,
    string Module,
    string Reference,
    string Title,
    string Description,
    string ActorName,
    DateTimeOffset OccurredAtUtc,
    string Route);
