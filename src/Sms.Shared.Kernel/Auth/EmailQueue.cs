using System.Threading.Channels;

namespace Sms.Shared.Kernel.Auth;

/// Unbounded in-memory queue over System.Threading.Channels. Singleton.
public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateUnbounded<EmailMessage>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

    public ValueTask<EmailMessage> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
