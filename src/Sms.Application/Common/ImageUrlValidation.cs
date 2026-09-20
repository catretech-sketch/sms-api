using Sms.Shared.Kernel.Results;

namespace Sms.Application.Common;

/// <summary>Shared rules for photo/logo fields stored as a data URI or http(s) URL
/// directly on a row (no blob storage) — same limits used by Tenants.LogoUrl,
/// Users.PhotoUrl, and Students.PhotoUrl.</summary>
public static class ImageUrlValidation
{
    public static Error? Validate(string? photoUrl)
    {
        if (photoUrl is { Length: > 400_000 })
            return new Error("invalid_request", "photo is too large (max ~300KB)");
        if (photoUrl is { Length: > 0 } &&
            !photoUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) &&
            !photoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !photoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new Error("invalid_request", "photo must be an image data URL or http(s) URL");
        return null;
    }

    /// <summary>Normalizes a validated value for storage: blank/whitespace-only becomes
    /// null (clears the photo), everything else is trimmed.</summary>
    public static string? Normalize(string? photoUrl) =>
        string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl.Trim();
}
