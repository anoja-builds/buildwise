namespace BuildWise.Api.DTOs;

/// <summary>Generic paginated list envelope so React's pagination UI has a real total count to work from.</summary>
public record PagedResultDto<T>(
    List<T> Items,
    int Total,
    int Page,
    int PageSize
);
