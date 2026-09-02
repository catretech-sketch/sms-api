namespace Sms.Modules.Staffing.Contracts;

public sealed record StaffDocumentResponse(Guid Id, string Label, string Value, bool? Ok);

public sealed record ProfileResponse(IReadOnlyList<StaffDocumentResponse> Documents);

// Admin/principal document management (StaffController's staff/{id}/documents routes).
// Update is a full replace of Label/Value/Ok, not a sparse patch — avoids the null-vs-omitted
// ambiguity a nullable partial-update would introduce for no requested benefit here.
public sealed record CreateStaffDocumentRequest(string Label, string Value, bool? Ok);

public sealed record UpdateStaffDocumentRequest(string Label, string Value, bool? Ok);
