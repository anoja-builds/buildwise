public record UpdateUserActiveRequestDto(bool IsActive);

public record UpdateUserRolesRequestDto(List<string> Roles);

public record AdminUserDto(
    int Id,
    string FullName,
    string Email,
    bool IsActive,
    List<string> Roles,
    DateTime CreatedAt
);

public record AuditLogDto(
    int Id,
    int? UserId,
    string Action,
    string? EntityType,
    string? EntityId,
    string HttpMethod,
    string RequestPath,
    int StatusCode,
    string? IpAddress,
    DateTime CreatedAt
);

public record SystemHealthDto(
    string Status,
    bool Database,
    Dictionary<string, bool> Services,
    DateTime CheckedAtUtc
);
