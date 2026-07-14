namespace Sms.Shared.Kernel.Auth;

/// A queued email to be delivered out-of-band by the EmailDispatchWorker.
public sealed record EmailMessage(
    string To,
    string Subject,
    string Body,
    byte[]? AttachmentBytes = null,
    string? AttachmentFileName = null,
    string? AttachmentContentType = null);
