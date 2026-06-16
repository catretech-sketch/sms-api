using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class EmailQueueTests
{
    [Fact]
    public async Task Dequeue_returns_the_enqueued_message()
    {
        var queue = new EmailQueue();
        var message = new EmailMessage("user@x.com", "Your code", "123456");

        queue.Enqueue(message);
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        dequeued.Should().Be(message);
    }
}
