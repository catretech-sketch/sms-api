using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class ConsoleOtpSenderTests
{
    [Fact]
    public async Task Generates_six_digit_code_and_reports_sent()
    {
        var sender = new ConsoleOtpSender();
        var code = await sender.SendAsync("+919999999999");
        code.Should().MatchRegex("^[0-9]{6}$");
    }
}
