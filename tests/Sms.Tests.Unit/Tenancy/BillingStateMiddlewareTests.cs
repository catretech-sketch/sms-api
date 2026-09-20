using FluentAssertions;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class BillingStateMiddlewareTests
{
    [Theory]
    [InlineData("active", "POST", false, "/v1/students", 0)]
    [InlineData("trial", "POST", false, "/v1/students", 403)]
    [InlineData("trial", "GET", false, "/v1/students", 403)]
    [InlineData("trial", "POST", false, "/v1/me/schools", 0)]
    [InlineData("trial", "POST", false, "/v1/me/schools/00000000-0000-0000-0000-000000000001/upgrade-requests", 0)]
    [InlineData("past_due", "GET", false, "/v1/students", 0)]
    [InlineData("past_due", "POST", false, "/v1/students", 402)]
    [InlineData("suspended", "GET", false, "/v1/students", 403)]
    [InlineData("suspended", "POST", false, "/v1/students", 403)]
    [InlineData("hold", "GET", false, "/v1/students", 403)]
    [InlineData("hold", "GET", false, "/v1/me/schools", 0)]
    [InlineData("deactivated", "POST", false, "/v1/students", 403)]
    [InlineData("deactivated", "GET", false, "/v1/me/schools", 0)]
    [InlineData("suspended", "GET", false, "/v1/me/schools", 0)]
    [InlineData("suspended", "POST", true, "/v1/students", 0)]   // platform exempt
    [InlineData("trial", "POST", true, "/v1/students", 0)]       // platform exempt
    public void Decides_block_code(string status, string method, bool isPlatform, string path, int expected)
    {
        BillingStateMiddleware.BlockCode(status, method, isPlatform, path)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("suspended", "POST", "/v1/auth/otp/request")]
    [InlineData("past_due", "POST", "/v1/auth/login")]
    [InlineData("trial", "POST", "/v1/auth/login")]
    public void Auth_paths_are_never_blocked(string status, string method, string path) =>
        BillingStateMiddleware.BlockCode(status, method, isPlatform: false, path).Should().Be(0);
}
