namespace Sms.Modules.Staffing.Contracts;

public sealed record StaffDocumentResponse(Guid Id, string Label, string Value, bool? Ok);

/// LicenseNumber/LicenseExpiry/EmergencyContact* are sourced from dbo.PersonExtras
/// (transport.license/transport.licenseExpiry, emergency.person/emergency.phone) — the same
/// opaque JSON blob the CRM's staff editor (sms-admin) already reads and writes. There is no
/// dedicated Staff column for these; they're plain strings straight out of that JSON, not
/// re-parsed or re-typed.
public sealed record ProfileResponse(
    IReadOnlyList<StaffDocumentResponse> Documents,
    string? LicenseNumber = null, string? LicenseExpiry = null,
    string? EmergencyContactName = null, string? EmergencyContactPhone = null);

// Admin/principal document management (StaffController's staff/{id}/documents routes).
// Update is a full replace of Label/Value/Ok, not a sparse patch — avoids the null-vs-omitted
// ambiguity a nullable partial-update would introduce for no requested benefit here.
public sealed record CreateStaffDocumentRequest(string Label, string Value, bool? Ok);

public sealed record UpdateStaffDocumentRequest(string Label, string Value, bool? Ok);
