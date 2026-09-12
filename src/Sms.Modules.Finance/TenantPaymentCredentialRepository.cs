using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Finance;

public sealed record TenantPaymentCredentialRow(
    Guid TenantId, string Provider, string? KeyId, string? KeySecretEncrypted,
    string? WebhookSecretEncrypted, string Mode, bool IsEnabled);

public sealed class TenantPaymentCredentialRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<TenantPaymentCredentialRow?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        (await QueryInlineAsync<TenantPaymentCredentialRow>(
            "SELECT TenantId, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled " +
            "FROM dbo.TenantPaymentCredentials WHERE TenantId = @tenantId AND Provider = 'razorpay'",
            new { tenantId }, ct)).FirstOrDefault();

    public Task UpsertAsync(
        Guid tenantId, string? keyId, string? keySecretEncrypted, string? webhookSecretEncrypted,
        string? mode, bool? isEnabled, bool hasNewKeySecret, bool hasNewWebhookSecret, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            """
            MERGE dbo.TenantPaymentCredentials AS target
            USING (SELECT @tenantId AS TenantId) AS src ON target.TenantId = src.TenantId AND target.Provider = 'razorpay'
            WHEN MATCHED THEN UPDATE SET
                KeyId = COALESCE(@keyId, target.KeyId),
                KeySecretEncrypted = CASE WHEN @hasNewKeySecret = 1 THEN @keySecretEncrypted ELSE target.KeySecretEncrypted END,
                WebhookSecretEncrypted = CASE WHEN @hasNewWebhookSecret = 1 THEN @webhookSecretEncrypted ELSE target.WebhookSecretEncrypted END,
                Mode = COALESCE(@mode, target.Mode), IsEnabled = COALESCE(@isEnabled, target.IsEnabled), UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (TenantId, Provider, KeyId, KeySecretEncrypted, WebhookSecretEncrypted, Mode, IsEnabled)
                VALUES (@tenantId, 'razorpay', @keyId, @keySecretEncrypted, @webhookSecretEncrypted,
                    COALESCE(@mode, 'test'), COALESCE(@isEnabled, 0));
            """,
            new { tenantId, keyId, keySecretEncrypted, webhookSecretEncrypted, mode, isEnabled, hasNewKeySecret, hasNewWebhookSecret },
            ct);
}
