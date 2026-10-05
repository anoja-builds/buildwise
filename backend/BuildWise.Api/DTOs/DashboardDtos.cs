namespace BuildWise.Api.DTOs;

public record DashboardMetricDto(
    string Key,
    string Label,
    decimal Value,
    string? Suffix = null
);

public record DashboardTaskDto(
    string Key,
    string Title,
    string Description,
    string Route,
    string Priority
);

public record DashboardActivityDto(
    int Id,
    string Title,
    string Detail,
    DateTime CreatedAt
);

public record DashboardAlertDto(
    string Severity,
    string Title,
    string Detail,
    string Route
);

public record DashboardResponseDto(
    string PrimaryRole,
    IReadOnlyList<string> Roles,
    IReadOnlyList<DashboardMetricDto> Metrics,
    IReadOnlyList<DashboardTaskDto> Tasks,
    IReadOnlyList<DashboardActivityDto> Activity,
    IReadOnlyList<DashboardAlertDto> Alerts,
    DateTime GeneratedAtUtc
);
