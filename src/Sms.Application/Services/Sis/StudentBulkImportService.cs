using Sms.Application.Common;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Transport;
using Sms.Modules.Academics.Contracts;
using Sms.Modules.Sis.Contracts;
using Sms.Modules.Sis.Data;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Sis;

public interface IStudentBulkImportService
{
    Task<ApiResult<BulkImportBatchResponse>> ProcessBatchAsync(BulkImportBatchRequest req, CancellationToken ct = default);
}

public sealed class StudentBulkImportService(
    ISisService sis, IAcademicsService academics, IStudentTransportService transport,
    BulkImportRepository repo, ITenantContext tenant) : IStudentBulkImportService
{
    public async Task<ApiResult<BulkImportBatchResponse>> ProcessBatchAsync(
        BulkImportBatchRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<BulkImportBatchResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var existing = await repo.GetExistingResultAsync(tid, req.ImportId, req.BatchIndex, ct);
        if (existing is not null)
            return ApiResult<BulkImportBatchResponse>.Ok(existing);

        var results = new List<BulkImportRowResult>();
        foreach (var row in req.Rows)
        {
            results.Add(await ProcessRowAsync(row, ct));
        }

        var response = new BulkImportBatchResponse(
            req.ImportId, req.BatchIndex,
            Processed: results.Count,
            Created: results.Count(r => r.Status == "created"),
            Skipped: results.Count(r => r.Status == "skipped"),
            TransportPending: results.Count(r => r.TransportStatus == "pending"),
            Rows: results);

        var recorded = await repo.RecordResultAsync(tid, req.ImportId, req.BatchIndex, response, ct);
        return ApiResult<BulkImportBatchResponse>.Ok(recorded);
    }

    /// Minimal required-field guard before creating — the backend's own final authority,
    /// independent of whatever the client's Preview step already checked. Deliberately does
    /// NOT re-implement phone/email format regex or duplicate-vs-roster checks (those stay
    /// client-only per the design) — only guards against structurally incomplete rows that
    /// dbo.Student_Create would otherwise silently accept.
    private static string? RequiredFieldError(CreateStudentRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required";
        if (string.IsNullOrWhiteSpace(r.Grade)) return "Class is required";
        if (string.IsNullOrWhiteSpace(r.Section)) return "Section is required";
        if (r.Gender is not ("M" or "F")) return "Gender must be M or F";
        if (r.Dob is null) return "Date of birth is required";
        if (string.IsNullOrWhiteSpace(r.Email)) return "Email is required";
        if (string.IsNullOrWhiteSpace(r.GuardianPhone)) return "Primary contact number is required";
        if (string.IsNullOrWhiteSpace(r.GuardianName)) return "Father or mother name is required";
        return null;
    }

    private async Task<BulkImportRowResult> ProcessRowAsync(BulkImportRowRequest row, CancellationToken ct)
    {
        try
        {
            if (RequiredFieldError(row.CreateStudentRequest) is { } fieldError)
                return new BulkImportRowResult(row.RowNumber, null, "skipped", fieldError, null);

            var created = await sis.CreateStudentAsync(row.CreateStudentRequest, ct);
            if (!created.IsSuccess || created.Data is null)
                return new BulkImportRowResult(row.RowNumber, null, "skipped", created.Error?.Message ?? "Could not create student", null);

            var studentId = created.Data.Id;

            if (!string.IsNullOrWhiteSpace(row.ExtrasJson) && row.ExtrasJson != "{}")
            {
                await academics.UpsertPersonExtrasAsync("student", studentId, new UpsertPersonExtrasRequest(row.ExtrasJson), ct);
                // Best-effort, same as single Add: an extras failure never un-creates the student.
            }

            string? transportStatus = "not_applicable";
            if (row.Transport is { OptedIn: true } t)
            {
                var transportResult = await transport.SetAsync(
                    studentId, new SetStudentTransportRequest(true, t.RouteId, t.StopId, t.FeeHeadId), ct);
                transportStatus = transportResult.IsSuccess ? transportResult.Data?.Status : "pending";
            }

            return new BulkImportRowResult(row.RowNumber, studentId, "created", null, transportStatus);
        }
        catch (Exception ex)
        {
            // One bad row must never abort the batch — record it as skipped and move on.
            return new BulkImportRowResult(row.RowNumber, null, "skipped", ex.Message, null);
        }
    }
}
