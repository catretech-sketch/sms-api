using FluentAssertions;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class BillingStateMiddlewareTests
{
    [Theory]
    [InlineData("active", "POST", false, 0)]
    [InlineData("trial", "POST", false, 0)]
    [InlineData("past_due", "GET", false, 0)]
    [InlineData("past_due", "POST", false, 402)]
    [InlineData("suspended", "GET", false, 403)]
    [InlineData("suspended", "POST", false, 403)]
    [InlineData("suspended", "POST", true, 0)]   // platform exempt
    public void Decides_block_code(string status, string method, bool isPlatform, int expected)
    {
        BillingStateMiddleware.BlockCode(status, method, isPlatform, path: "/v1/students")
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("suspended", "POST", "/v1/auth/otp/request")] // auth always allowed
    [InlineData("past_due", "POST", "/v1/auth/login")]
    public void Auth_paths_are_never_blocked(string status, string method, string path) =>
        BillingStateMiddleware.BlockCode(status, method, isPlatform: false, path).Should().Be(0);
}
