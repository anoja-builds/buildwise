using BuildWise.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

public class SmtpEmailServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("supplier@example.com")]
    public async Task Unconfigured_or_empty_recipient_does_not_claim_delivery(string recipient)
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new SmtpEmailService(configuration, NullLogger<SmtpEmailService>.Instance);

        Assert.False(await service.SendAsync(recipient, "RFQ invitation", "Requested materials"));
    }
}
