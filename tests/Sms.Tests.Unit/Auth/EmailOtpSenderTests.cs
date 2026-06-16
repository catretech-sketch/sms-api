using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class EmailOtpSenderTests
{
    private sealed class CapturingQueue : IEmailQueue
    {
        public EmailMessage? Last { get; private set; }
        public void Enqueue(EmailMessage message) => Last = message;
        public ValueTask<EmailMessage> DequeueAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Returns_a_six_digit_code()
    {
        var sender = new EmailOtpSender(new CapturingQueue());

        var code = await sender.SendAsync("user@x.com", "email");

        code.Should().MatchRegex("^[0-9]{6}$");
    }

    [Fact]
    public async Task Enqueues_email_addressed_to_identifier_containing_the_code()
    {
        var queue = new CapturingQueue();
        var sender = new EmailOtpSender(queue);

        var code = await sender.SendAsync("user@x.com", "email");

        queue.Last.Should().NotBeNull();
        queue.Last!.To.Should().Be("user@x.com");
        queue.Last.Body.Should().Contain(code);
    }
}
