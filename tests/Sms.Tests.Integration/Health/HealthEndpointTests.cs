using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Sms.Tests.Integration.Health;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("environment", "Production")); // skip dev migrations
        var client = app.CreateClient();
        var res = await client.GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
