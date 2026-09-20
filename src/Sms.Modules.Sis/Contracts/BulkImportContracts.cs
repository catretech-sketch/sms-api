namespace Sms.Modules.Sis.Contracts;

public sealed record BulkImportTransportInput(bool OptedIn, Guid? RouteId, Guid? StopId, Guid? FeeHeadId);

public sealed record BulkImportRowRequest(
    int RowNumber,
    CreateStudentRequest CreateStudentRequest,
    string? ExtrasJson,
    BulkImportTransportInput? Transport);

public sealed record BulkImportBatchRequest(Guid ImportId, int BatchIndex, IReadOnlyList<BulkImportRowRequest> Rows);

public sealed record BulkImportRowResult(int RowNumber, Guid? StudentId, string Status, string? Error, string? TransportStatus);

public sealed record BulkImportBatchResponse(
    Guid ImportId, int BatchIndex, int Processed, int Created, int Skipped, int TransportPending,
    IReadOnlyList<BulkImportRowResult> Rows);
