using FluentAssertions;
using Sms.Shared.Kernel.Payments;
using Xunit;

namespace Sms.Tests.Unit.Payments;

public class RazorpayClientTests
{
    [Fact]
    public void VerifyPaymentSignature_accepts_a_correctly_signed_payload()
    {
        const string secret = "test-secret";
        const string orderId = "order_ABC";
        const string paymentId = "pay_XYZ";
        var expected = ComputeHmac(secret, $"{orderId}|{paymentId}");

        RazorpayClient.VerifyPaymentSignature(secret, orderId, paymentId, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifyPaymentSignature_rejects_a_tampered_signature()
    {
        const string secret = "test-secret";
        RazorpayClient.VerifyPaymentSignature(secret, "order_ABC", "pay_XYZ", "not-a-real-signature").Should().BeFalse();
    }

    [Fact]
    public void VerifyPaymentSignature_rejects_the_wrong_secret()
    {
        const string orderId = "order_ABC";
        const string paymentId = "pay_XYZ";
        var signedWithOtherSecret = ComputeHmac("some-other-secret", $"{orderId}|{paymentId}");

        RazorpayClient.VerifyPaymentSignature("test-secret", orderId, paymentId, signedWithOtherSecret).Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_accepts_a_correctly_signed_body()
    {
        const string secret = "webhook-secret";
        const string body = """{"event":"payment.captured"}""";
        var expected = ComputeHmac(secret, body);

        RazorpayClient.VerifyWebhookSignature(secret, body, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_rejects_a_tampered_body()
    {
        const string secret = "webhook-secret";
        var signature = ComputeHmac(secret, """{"event":"payment.captured"}""");

        RazorpayClient.VerifyWebhookSignature(secret, """{"event":"payment.failed"}""", signature).Should().BeFalse();
    }

    private static string ComputeHmac(string secret, string payload)
    {
        var key = System.Text.Encoding.UTF8.GetBytes(secret);
        var data = System.Text.Encoding.UTF8.GetBytes(payload);
        var hash = System.Security.Cryptography.HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
