using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class ChannelOtpSenderTests
{
    private sealed class CapturingQueue : IEmailQueue
    {
        public EmailMessage? Last { get; private set; }
        public void Enqueue(EmailMessage message) => Last = message;
        public ValueTask<EmailMessage> DequeueAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Email_channel_routes_through_the_email_sender()
    {
        var queue = new CapturingQueue();
        var router = new ChannelOtpSender(new EmailOtpSender(queue), new ConsoleOtpSender());

        await router.SendAsync("user@x.com", "email");

        queue.Last.Should().NotBeNull("the email channel must enqueue an email");
    }

    [Fact]
    public async Task Sms_channel_does_not_use_the_email_sender()
    {
        var queue = new CapturingQueue();
        var router = new ChannelOtpSender(new EmailOtpSender(queue), new ConsoleOtpSender());

        var code = await router.SendAsync("+15551234", "sms");

        queue.Last.Should().BeNull("the sms channel must not enqueue an email");
        code.Should().MatchRegex("^[0-9]{6}$");
    }
}
