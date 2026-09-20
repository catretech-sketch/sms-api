namespace Sms.Shared.Kernel.Auth;

public sealed record EmailAttachment(
    byte[] Bytes,
    string FileName,
    string ContentType = "application/octet-stream");

/// A queued email to be delivered out-of-band by the EmailDispatchWorker.
public sealed record EmailMessage(
    string To,
    string Subject,
    string Body,
    byte[]? AttachmentBytes = null,
    string? AttachmentFileName = null,
    string? AttachmentContentType = null,
    string? HtmlBody = null,
    IReadOnlyList<EmailAttachment>? ExtraAttachments = null);
