using BuildWise.Api.Services;

namespace BuildWise.Api.Tests;

/// <summary>Test double so workflow/scenario tests don't need real SMTP configuration.</summary>
public class NoOpEmailService : IEmailService
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
