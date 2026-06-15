using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class ConsoleOtpSenderTests
{
    [Fact]
    public async Task Generates_a_six_digit_code_for_any_channel()
    {
        var sender = new ConsoleOtpSender();
        var code = await sender.SendAsync("user@x.com", "email");
        code.Should().MatchRegex("^[0-9]{6}$");
    }
}
