using Microsoft.AspNetCore.DataProtection;
using Sms.Modules.Finance;

namespace Sms.Application.Services.Finance;

public sealed record TenantRazorpayCredentials(string KeyId, string KeySecret, string WebhookSecret, string Mode);
public sealed record UpsertTenantRazorpayRequest(string? KeyId, string? KeySecret, string? WebhookSecret, string Mode, bool IsEnabled);
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
        return new TenantRazorpayCredentials(
            row.KeyId,
            Protector.Unprotect(row.KeySecretEncrypted),
            row.WebhookSecretEncrypted is null ? "" : Protector.Unprotect(row.WebhookSecretEncrypted),
            row.Mode);
    }

    public async Task<TenantRazorpayCredentialStatus> UpsertAsync(
        Guid tenantId, UpsertTenantRazorpayRequest req, CancellationToken ct = default)
    {
        var hasNewKeySecret = !string.IsNullOrEmpty(req.KeySecret);
        var hasNewWebhookSecret = !string.IsNullOrEmpty(req.WebhookSecret);
        await repo.UpsertAsync(
            tenantId, req.KeyId,
            hasNewKeySecret ? Protector.Protect(req.KeySecret!) : null,
            hasNewWebhookSecret ? Protector.Protect(req.WebhookSecret!) : null,
            req.Mode, req.IsEnabled, hasNewKeySecret, hasNewWebhookSecret, ct);
        return await GetStatusAsync(tenantId, ct);
    }

    public async Task<TenantRazorpayCredentialStatus> GetStatusAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await repo.GetAsync(tenantId, ct);
        if (row is null)
            return new TenantRazorpayCredentialStatus(false, null, "test", "not_configured", false, false);
        var configured = row.KeyId is not null && row.KeySecretEncrypted is not null;
        return new TenantRazorpayCredentialStatus(
            row.IsEnabled, row.KeyId, row.Mode, configured ? "configured" : "not_configured",
            row.KeySecretEncrypted is not null, row.WebhookSecretEncrypted is not null);
    }
}
