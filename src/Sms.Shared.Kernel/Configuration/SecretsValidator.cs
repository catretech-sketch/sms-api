using System.Text;

namespace Sms.Shared.Kernel.Configuration;

/// Fail-fast configuration guard: refuses to boot with a missing/placeholder/weak JWT signing key
/// or a missing connection string. Call once at startup, before building the app.
public static class SecretsValidator
{
    /// The placeholder shipped in source control — never valid at runtime.
    public const string PlaceholderKey = "CHANGE-ME-in-user-secrets-at-least-32-bytes-long!!";
    public const int MinKeyBytes = 32;

    public static void Validate(string? signingKey, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Configuration error: ConnectionStrings:Sql is not set. Provide it via the ConnectionStrings__Sql environment variable.");

        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException(
                "Configuration error: Jwt:SigningKey is not set. Provide a >= 32-byte key via the Jwt__SigningKey environment variable.");

        if (signingKey == PlaceholderKey)
            throw new InvalidOperationException(
                "Configuration error: Jwt:SigningKey is still the placeholder. Set a real >= 32-byte key.");

        if (Encoding.UTF8.GetByteCount(signingKey) < MinKeyBytes)
            throw new InvalidOperationException(
                $"Configuration error: Jwt:SigningKey must be at least {MinKeyBytes} bytes.");
    }
}
