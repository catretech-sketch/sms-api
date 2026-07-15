using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sms.Shared.Kernel.Payments;

public sealed class RazorpayGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<RazorpayOptions> options,
    ILogger<RazorpayGateway> log) : IRazorpayGateway
{
    private readonly RazorpayOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;
    public string KeyId => _opts.KeyId;

    public async Task<RazorpayOrderCreated> CreateOrderAsync(
        long amountPaise, string currency, string receipt, CancellationToken ct = default)
    {
        if (!_opts.IsConfigured)
            throw new InvalidOperationException("Razorpay is not configured.");

        var client = httpClientFactory.CreateClient("razorpay");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.KeyId}:{_opts.KeySecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        var payload = JsonSerializer.Serialize(new
        {
            amount = amountPaise,
            currency,
            receipt = receipt.Length > 40 ? receipt[..40] : receipt,
            payment_capture = 1,
        });
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Razorpay order create failed: {Status} {Body}", (int)res.StatusCode, body);
            throw new InvalidOperationException("Could not create Razorpay order.");
        }

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay order missing id.");
        var amount = doc.RootElement.GetProperty("amount").GetInt64();
        var curr = doc.RootElement.GetProperty("currency").GetString() ?? currency;
        return new RazorpayOrderCreated(id, amount, curr);
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentId) || string.IsNullOrWhiteSpace(signature))
            return false;
        var payload = $"{orderId}|{paymentId}";
        return HmacEquals(payload, signature, _opts.KeySecret);
    }

    public bool VerifyWebhookSignature(string body, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_opts.WebhookSecret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;
        return HmacEquals(body, signatureHeader, _opts.WebhookSecret);
    }

    private static bool HmacEquals(string payload, string signatureHex, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        var actual = signatureHex.Trim().ToLowerInvariant();
        if (expected.Length != actual.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
