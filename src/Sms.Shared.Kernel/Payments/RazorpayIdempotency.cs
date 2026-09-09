namespace Sms.Shared.Kernel.Payments;

/// PayFeeInvoiceRequest.IdempotencyKey is a Guid, but Razorpay payment ids are opaque strings
/// (e.g. "pay_ABC123"). Both the client-verify path and the webhook path must derive the SAME
/// Guid from the SAME payment id so a race between them is caught by the existing
/// (TenantId, IdempotencyKey) unique constraint — hence one shared, deterministic derivation.
public static class RazorpayIdempotency
{
    public static Guid KeyFor(string razorpayPaymentId) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(razorpayPaymentId)));
}
