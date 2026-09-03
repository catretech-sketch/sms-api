namespace Sms.Modules.Staffing.Contracts;

public sealed record StaffDocumentResponse(Guid Id, string Label, string Value, bool? Ok);

public sealed record ProfileResponse(
    IReadOnlyList<StaffDocumentResponse> Documents,
    string? LicenseNumber = null, DateTime? LicenseExpiry = null,
    string? EmergencyContactName = null, string? EmergencyContactPhone = null);

/// Row shape for dbo.Staff_GetProfileFields — zero rows means the caller has no Staff record.
/// LicenseExpiry is DateTime, not DateOnly: Dapper (this version, no custom type handler
/// registered) materializes SQL `date` columns as DateTime, and fails to construct the record
/// at all if the constructor expects DateOnly instead.
public sealed record StaffProfileFieldsRow(
    string? LicenseNumber, DateTime? LicenseExpiry, string? EmergencyContactName, string? EmergencyContactPhone);

// Admin/principal document management (StaffController's staff/{id}/documents routes).
// Update is a full replace of Label/Value/Ok, not a sparse patch — avoids the null-vs-omitted
// ambiguity a nullable partial-update would introduce for no requested benefit here.
public sealed record CreateStaffDocumentRequest(string Label, string Value, bool? Ok);

public sealed record UpdateStaffDocumentRequest(string Label, string Value, bool? Ok);
