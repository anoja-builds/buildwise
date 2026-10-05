using System.Net;
using System.Text.Json;

namespace BuildWise.Api.Middleware;

/// <summary>
/// Global safety net: turns any unhandled exception into a structured JSON
/// error response instead of a raw stack trace, and logs it once, centrally.
/// Controller-level try/catch still runs first for expected, specific failures
/// (validation, not-found, business-rule violations); this only catches what
/// falls through.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var payload = JsonSerializer.Serialize(new
            {
                error = "An unexpected error occurred. Please try again or contact support if the problem persists.",
                traceId = context.TraceIdentifier
            });

            await context.Response.WriteAsync(payload);
        }
    }
}
