using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class EmailDispatchWorkerTests
{
    private sealed class SignalingSender : IEmailSender
    {
        private readonly Func<EmailMessage, bool> _throwFor;
        private readonly TaskCompletionSource<EmailMessage> _delivered = new();
        public SignalingSender(Func<EmailMessage, bool>? throwFor = null) => _throwFor = throwFor ?? (_ => false);
        public Task<EmailMessage> Delivered => _delivered.Task;

        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            var msg = new EmailMessage(to, subject, body);
            if (_throwFor(msg)) throw new InvalidOperationException("smtp boom");
            _delivered.TrySetResult(msg);
            return Task.CompletedTask;
        }
    }

    private static EmailDispatchWorker NewWorker(IEmailQueue queue, IEmailSender sender) =>
        new(queue, sender, NullLogger<EmailDispatchWorker>.Instance, maxAttempts: 2, retryDelay: TimeSpan.Zero);

    [Fact]
    public async Task Delivers_a_queued_message_to_the_sender()
    {
        var queue = new EmailQueue();
        var sender = new SignalingSender();
        var worker = NewWorker(queue, sender);

        await worker.StartAsync(CancellationToken.None);
        queue.Enqueue(new EmailMessage("user@x.com", "Your code", "654321"));

        var delivered = await sender.Delivered.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        delivered.To.Should().Be("user@x.com");
        delivered.Body.Should().Be("654321");
    }

    [Fact]
    public async Task A_failing_send_does_not_stop_the_loop()
    {
        var queue = new EmailQueue();
        // Throw for the first message; deliver the second.
        var sender = new SignalingSender(m => m.Body == "first");
        var worker = NewWorker(queue, sender);

        await worker.StartAsync(CancellationToken.None);
        queue.Enqueue(new EmailMessage("a@x.com", "s", "first"));
        queue.Enqueue(new EmailMessage("b@x.com", "s", "second"));

        var delivered = await sender.Delivered.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        delivered.Body.Should().Be("second", "the loop must survive the failed first send");
    }
}
