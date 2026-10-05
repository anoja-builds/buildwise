namespace BuildWise.Api.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends a plain-text notification email. Never throws — a failed or
    /// unconfigured send is logged and swallowed so it can never block a
    /// procurement action (spec §11: handle timeouts/failures gracefully).
    /// Returns true only when SMTP accepted the message; false when skipped or failed.
    /// </summary>
    Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}
