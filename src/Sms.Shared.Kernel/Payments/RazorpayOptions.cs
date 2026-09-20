namespace Sms.Shared.Kernel.Payments;

public sealed class RazorpayOptions
{
    public const string SectionName = "Razorpay";
    public string KeyId { get; set; } = "";
    public string KeySecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
}
