using Sms.Shared.Kernel.Auth;

namespace Sms.Tests.Unit.Comms;

public sealed class AnnouncementEmailDispatchTests
{
    private sealed class CapturingQueue : IEmailQueue
    {
        public List<EmailMessage> Items { get; } = [];
        public void Enqueue(EmailMessage message) => Items.Add(message);
        public ValueTask<EmailMessage> DequeueAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void Enqueue_sends_modern_mail_with_optional_pdf()
    {
        var q = new CapturingQueue();
        var pdf = new byte[] { 1, 2, 3, 4 };
        var n = AnnouncementEmailDispatch.Enqueue(
            q,
            ["a@school.test", "b@school.test", "A@school.test", "  "],
            "scc · PTM: teste ptm",
            "plain body",
            "<p>html</p>",
            pdf,
            "Catre-Notice-PTM.pdf");

        Assert.Equal(2, n);
        Assert.Equal(2, q.Items.Count);
        Assert.All(q.Items, m =>
        {
            Assert.Contains("PTM", m.Subject, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("<p>html</p>", m.HtmlBody);
            Assert.Equal("Catre-Notice-PTM.pdf", m.AttachmentFileName);
            Assert.Equal(pdf, m.AttachmentBytes);
        });
    }
}
