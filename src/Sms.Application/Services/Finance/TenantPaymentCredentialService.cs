using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Sms.Modules.Finance;

namespace Sms.Application.Services.Finance;

public sealed record TenantRazorpayCredentials(string KeyId, string KeySecret, string WebhookSecret, string Mode);

/// KeyId/Mode/IsEnabled: null means "omitted, leave unchanged" (partial-body PUT support).
/// KeySecret/WebhookSecret: null means "omitted, leave unchanged"; an explicit empty string
/// ("") means "clear it" — the stored encrypted value is set back to NULL, not re-encrypted
/// as an empty string.
public sealed record UpsertTenantRazorpayRequest(string? KeyId, string? KeySecret, string? WebhookSecret, string? Mode, bool? IsEnabled);
public sealed record TenantRazorpayCredentialStatus(
    bool Enabled, string? KeyId, string Mode, string Status, bool KeySecretSet, bool WebhookSecretSet);

public interface ITenantPaymentCredentialService
{
    Task<TenantRazorpayCredentials?> GetActiveAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantRazorpayCredentialStatus> UpsertAsync(Guid tenantId, UpsertTenantRazorpayRequest req, CancellationToken ct = default);
    Task<TenantRazorpayCredentialStatus> GetStatusAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class TenantPaymentCredentialService(
    TenantPaymentCredentialRepository repo, IDataProtectionProvider dataProtection) : ITenantPaymentCredentialService
{
    private const string Purpose = "TenantPaymentCredentials.Razorpay.v1";
    private IDataProtector Protector => dataProtection.CreateProtector(Purpose);

    public async Task<TenantRazorpayCredentials?> GetActiveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await repo.GetAsync(tenantId, ct);
        if (row is null || !row.IsEnabled || row.KeyId is null || row.KeySecretEncrypted is null)
            return null;
        try
        {
            return new TenantRazorpayCredentials(
                row.KeyId,
                Protector.Unprotect(row.KeySecretEncrypted),
                row.WebhookSecretEncrypted is null ? "" : Protector.Unprotect(row.WebhookSecretEncrypted),
                row.Mode);
        }
        catch (CryptographicException)
        {
            // The DataProtection key ring that encrypted this secret is gone (or never persisted —
            // e.g. a restart/redeploy/scale-out lost it). Treat this exactly like "not configured"
            // so callers fall back to their existing clean "payment_gateway_not_configured"/403
            // error instead of an uncaught 500 on every webhook/verify/order-create call.
            return null;
        }
    }

    public async Task<TenantRazorpayCredentialStatus> UpsertAsync(
        Guid tenantId, UpsertTenantRazorpayRequest req, CancellationToken ct = default)
    {
        // null = omitted (leave unchanged); "" = explicit clear (store NULL); non-empty = new value.
        var hasNewKeySecret = req.KeySecret is not null;
        var hasNewWebhookSecret = req.WebhookSecret is not null;
        await repo.UpsertAsync(
            tenantId, req.KeyId,
            hasNewKeySecret ? (req.KeySecret!.Length == 0 ? null : Protector.Protect(req.KeySecret)) : null,
            hasNewWebhookSecret ? (req.WebhookSecret!.Length == 0 ? null : Protector.Protect(req.WebhookSecret)) : null,
            req.Mode, req.IsEnabled, hasNewKeySecret, hasNewWebhookSecret, ct);
        return await GetStatusAsync(tenantId, ct);
    }

    public async Task<TenantRazorpayCredentialStatus> GetStatusAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await repo.GetAsync(tenantId, ct);
        if (row is null)
            return new TenantRazorpayCredentialStatus(false, null, "test", "not_configured", false, false);
        // "configured" requires a webhook secret too, not just key id + key secret: without one every
        // webhook delivery fails closed (400) forever with no other signal to the school. This only
        // checks presence of the encrypted column, not that it's still decryptable under the current
        // DataProtection key ring — doing a real decrypt-and-catch here as well would mean every GET
        // /school/integrations pays a decrypt cost and duplicates the fail-closed handling that already
        // lives in GetActiveAsync (the actually load-bearing path); the status surfaced here is a
        // best-effort health signal, not a guarantee, and key_secret_set/webhook_secret_set already
        // let the UI show something was saved even if it can no longer be decrypted.
        var configured = row.KeyId is not null && row.KeySecretEncrypted is not null && row.WebhookSecretEncrypted is not null;
        return new TenantRazorpayCredentialStatus(
            row.IsEnabled, row.KeyId, row.Mode, configured ? "configured" : "not_configured",
            row.KeySecretEncrypted is not null, row.WebhookSecretEncrypted is not null);
    }
}
