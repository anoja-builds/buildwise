using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using System.Text.Json;

namespace BuildWise.Api.Middleware;

public sealed class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;
    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger) { _next = next; _logger = logger; }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method) && !HttpMethods.IsPatch(context.Request.Method) && !HttpMethods.IsDelete(context.Request.Method)) return;
        if (context.User.Identity?.IsAuthenticated != true) return;
        try
        {
            using var scope = context.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId, Action = $"{context.Request.Method} {context.Request.Path}", HttpMethod = context.Request.Method,
                RequestPath = context.Request.Path.ToString(), StatusCode = context.Response.StatusCode,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                MetadataJson = JsonSerializer.Serialize(new { traceId = context.TraceIdentifier })
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist audit event for {Path}.", context.Request.Path); }
    }
}
