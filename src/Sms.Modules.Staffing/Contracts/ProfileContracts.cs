namespace Sms.Modules.Staffing.Contracts;

public sealed record StaffDocumentResponse(Guid Id, string Label, string Value, bool? Ok);

public sealed record ProfileResponse(IReadOnlyList<StaffDocumentResponse> Documents);
