using Microsoft.Extensions.Logging;
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
    BulkImportRepository repo, ITenantContext tenant, ILogger<StudentBulkImportService> logger) : IStudentBulkImportService
{
    public async Task<ApiResult<BulkImportBatchResponse>> ProcessBatchAsync(
        BulkImportBatchRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<BulkImportBatchResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        // [ApiController]'s automatic model validation is disabled for this action
        // (see SkipModelValidationAttribute) so that one nested row's bad field (e.g. a null
        // CreateStudentRequest.Name) doesn't reject the whole batch with a framework-level 400.
        // That means the framework no longer catches `rows` itself being null/missing for free —
        // this guard replaces that lost protection so a missing `rows` field returns a clean 400
        // instead of an unhandled NullReferenceException from the loop below.
        if (req.Rows is null or { Count: 0 })
            return ApiResult<BulkImportBatchResponse>.Fail(new Error("validation_error", "rows is required"), 400);

        var existing = await repo.GetExistingResultAsync(tid, req.ImportId, req.BatchIndex, ct);
        if (existing is not null)
            return ApiResult<BulkImportBatchResponse>.Ok(existing);

        var results = new List<BulkImportRowResult>();
        foreach (var row in req.Rows)
        {
            // A genuine cancellation (client disconnect/timeout) must propagate out of the loop
            // rather than being recorded as a false "skipped" row — see ProcessRowAsync.
            results.Add(await ProcessRowAsync(req.ImportId, req.BatchIndex, row, ct));
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

    private async Task<BulkImportRowResult> ProcessRowAsync(
        Guid importId, int batchIndex, BulkImportRowRequest row, CancellationToken ct)
    {
        Guid studentId;
        try
        {
            if (RequiredFieldError(row.CreateStudentRequest) is { } fieldError)
                return new BulkImportRowResult(row.RowNumber, null, "skipped", fieldError, null);

            var created = await sis.CreateStudentAsync(row.CreateStudentRequest, ct);
            if (!created.IsSuccess || created.Data is null)
                return new BulkImportRowResult(row.RowNumber, null, "skipped", created.Error?.Message ?? "Could not create student", null);

            studentId = created.Data.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad row must never abort the batch — record it as skipped and move on.
            // A genuine cancellation is NOT caught here (see `when` clause): it must propagate
            // so the batch never falsely records rows as "skipped" that were never actually
            // attempted, which would otherwise corrupt the batch's idempotency record.
            logger.LogError(ex,
                "Bulk import row {RowNumber} failed to create student in batch {BatchIndex} (import {ImportId})",
                row.RowNumber, batchIndex, importId);
            return new BulkImportRowResult(row.RowNumber, null, "skipped",
                "Could not create this student — see server logs for details", null);
        }

        // The student row is already committed from this point on — every path below must
        // return status "created" with this studentId, even if extras/transport blow up.
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(row.ExtrasJson) && row.ExtrasJson != "{}")
        {
            try
            {
                await academics.UpsertPersonExtrasAsync("student", studentId, new UpsertPersonExtrasRequest(row.ExtrasJson), ct);
                // Best-effort, same as single Add: an extras failure never un-creates the student.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Bulk import row {RowNumber} extras save failed in batch {BatchIndex} (import {ImportId}) for student {StudentId}",
                    row.RowNumber, batchIndex, importId, studentId);
                notes.Add("Extra details could not be saved — see server logs for details");
            }
        }

        string? transportStatus = "not_applicable";
        if (row.Transport is { OptedIn: true } t)
        {
            try
            {
                var transportResult = await transport.SetAsync(
                    studentId, new SetStudentTransportRequest(true, t.RouteId, t.StopId, t.FeeHeadId), ct);
                if (transportResult.IsSuccess)
                {
                    transportStatus = transportResult.Data?.Status;
                }
                else
                {
                    // A genuine transport failure (feature locked, bad route/stop/fee head) must
                    // be distinguishable from the legitimate "opted in, no bus has capacity yet"
                    // case — both would otherwise show as "pending" and mislead an admin.
                    transportStatus = "failed";
                    notes.Add($"Transport mapping failed: {transportResult.Error?.Message ?? "unknown error"}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Bulk import row {RowNumber} transport save failed in batch {BatchIndex} (import {ImportId}) for student {StudentId}",
                    row.RowNumber, batchIndex, importId, studentId);
                transportStatus = "failed";
                notes.Add("Transport mapping could not be saved — see server logs for details");
            }
        }

        return new BulkImportRowResult(row.RowNumber, studentId, "created",
            notes.Count > 0 ? string.Join("; ", notes) : null, transportStatus);
    }
}
