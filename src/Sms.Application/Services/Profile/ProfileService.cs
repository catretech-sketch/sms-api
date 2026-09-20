using System.Text.Json;
using Sms.Application.Common;
using Sms.Modules.Academics.Data;
using Sms.Modules.Staffing.Contracts;
using Sms.Modules.Staffing.Profile;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Profile;

public interface IProfileService
{
    Task<ApiResult<ProfileResponse>> GetAsync(CancellationToken ct = default);
}

public sealed class ProfileService(
    ProfileRepository repo, PersonExtrasRepository personExtras, ITenantContext tenant) : IProfileService
{
    public async Task<ApiResult<ProfileResponse>> GetAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<ProfileResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var documents = await repo.ListForUserAsync(tid, uid, ct);

        var staffId = await repo.GetStaffIdByUserIdAsync(tid, uid, ct);
        var (license, licenseExpiry, emName, emPhone) = staffId is { } sid
            ? ExtractTransportEmergency(await personExtras.GetAsync("staff", sid, ct))
            : (null, null, null, null);

        return ApiResult<ProfileResponse>.Ok(new ProfileResponse(documents, license, licenseExpiry, emName, emPhone));
    }

    /// dbo.PersonExtras is an opaque per-tenant JSON blob the CRM (sms-admin) staff editor
    /// already reads and writes — {"transport":{"license":...,"licenseExpiry":...},
    /// "emergency":{"person":...,"phone":...}}. Same source of truth, not a re-typed copy:
    /// malformed/missing JSON or absent keys just yield nulls, never an error.
    private static (string? License, string? LicenseExpiry, string? EmergencyName, string? EmergencyPhone)
        ExtractTransportEmergency(Sms.Modules.Academics.Contracts.PersonExtrasResponse? extras)
    {
        if (extras is null) return (null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(extras.ExtrasJson);
            var root = doc.RootElement;
            string? Str(JsonElement parent, string prop) =>
                parent.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            string? license = null, licenseExpiry = null, emName = null, emPhone = null;
            if (root.TryGetProperty("transport", out var transport) && transport.ValueKind == JsonValueKind.Object)
            {
                license = Str(transport, "license");
                licenseExpiry = Str(transport, "licenseExpiry");
            }
            if (root.TryGetProperty("emergency", out var emergency) && emergency.ValueKind == JsonValueKind.Object)
            {
                emName = Str(emergency, "person");
                emPhone = Str(emergency, "phone");
            }
            return (license, licenseExpiry, emName, emPhone);
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }
}
