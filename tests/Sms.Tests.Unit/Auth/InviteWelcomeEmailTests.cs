using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class InviteWelcomeEmailTests
{
    [Fact]
    public void Build_includes_school_name_role_and_otp()
    {
        var msg = InviteWelcomeEmail.Build("admin@demo.edu", "Greenwood High", "482913", "Admin");

        msg.To.Should().Be("admin@demo.edu");
        msg.Subject.Should().Be("Welcome to Greenwood High on SchoolMate");
        msg.Body.Should().Contain("Greenwood High");
        msg.Body.Should().Contain("as Admin");
        msg.Body.Should().Contain("482913");
        msg.Body.Should().Contain("Welcome");
        msg.Body.Should().Contain("forgot password");
    }

    [Fact]
    public void SmsBody_includes_school_and_code()
    {
        var sms = InviteWelcomeEmail.SmsBody("Greenwood High", "482913", "Owner");

        sms.Should().Contain("Greenwood High");
        sms.Should().Contain("482913");
        sms.Should().Contain("Owner");
    }
}
