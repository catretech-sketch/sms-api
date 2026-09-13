using System.Text.Json;
using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Modules.Sis.Contracts;
using Sms.Modules.Sis.Data;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Application.Services.Realtime;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public interface IFeeService
{
    Task<ApiResult<IReadOnlyList<FeePaymentResponse>>> ListPaymentsAsync(Guid? studentId, CancellationToken ct = default);
    Task<ApiResult<FeePaymentResponse>> CreatePaymentAsync(CreateFeePaymentRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FeeInvoiceResponse>>> ListInvoicesAsync(Guid? studentId, CancellationToken ct = default);
    Task<ApiResult<FeeInvoiceResponse>> CreateInvoiceAsync(CreateFeeInvoiceRequest req, CancellationToken ct = default);
    Task<FeeInvoiceResponse?> GetInvoiceAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<FeePaymentResponse>> PayInvoiceAsync(Guid id, PayFeeInvoiceRequest? req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<FeeHeadResponse>>> ListHeadsAsync(CancellationToken ct = default);
    Task<ApiResult<FeeHeadResponse>> CreateHeadAsync(CreateFeeHeadRequest req, CancellationToken ct = default);
    Task<ApiResult<FeeHeadResponse>> UpdateHeadAsync(Guid id, UpdateFeeHeadRequest req, CancellationToken ct = default);
    Task<ApiResult> DeleteHeadAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<FeeStructureResponse>> GetStructureAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FeeStructureSummaryResponse>>> ListStructureHistoryAsync(CancellationToken ct = default);
    Task<ApiResult<FeeStructureResponse>> GetStructureByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<FeeStructurePublishResponse>> PublishStructureAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<FeeStructurePublishResponse>> UnpublishStructureAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult> DeleteStructureAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<FeeStructureResponse>> UpsertStructureAsync(UpsertFeeStructureRequest req, CancellationToken ct = default);
    Task<ApiResult<GenerateFeeInvoicesResponse>> GenerateInvoicesAsync(
        GenerateFeeInvoicesRequest req, CancellationToken ct = default);
    Task<ApiResult<FeeReportSummaryResponse>> GetReportSummaryAsync(CancellationToken ct = default);
}

public sealed class FeeService(
    FeeRepository payments,
    FeeInvoiceRepository invoices,
    FeeHeadRepository heads,
    FeeStructureRepository structures,
    StudentRepository roster,
    Sms.Modules.Transport.StudentBusRepository studentBus,
    IPaymentGateway gateway,
    ITenantContext tenant,
    ILiveBroadcaster live) : IFeeService
{
    public async Task<ApiResult<IReadOnlyList<FeePaymentResponse>>> ListPaymentsAsync(Guid? studentId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeePaymentResponse>>.Ok(await payments.ListAsync(studentId, ct));

    public async Task<ApiResult<FeePaymentResponse>> CreatePaymentAsync(CreateFeePaymentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeePaymentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var mapped = req with
        {
            ClassLabel = FirstNonEmpty(req.ClassLabel, req.Cls),
            FeeType = FirstNonEmpty(req.FeeType, req.HeadName, "academic") ?? "academic",
            Method = FirstNonEmpty(req.Method, req.Mode),
        };
        var created = await payments.CreateAsync(tid, mapped, ct);
        await live.PublishAsync(tid, LiveEventTypes.Fees, ct: ct);
        return ApiResult<FeePaymentResponse>.Ok(created!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<FeeInvoiceResponse>>> ListInvoicesAsync(Guid? studentId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeeInvoiceResponse>>.Ok(await invoices.ListAsync(studentId, ct));

    public async Task<ApiResult<FeeInvoiceResponse>> CreateInvoiceAsync(CreateFeeInvoiceRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeInvoiceResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<FeeInvoiceResponse>.Ok((await invoices.CreateAsync(tid, req, ct))!, 201);
    }

    public Task<FeeInvoiceResponse?> GetInvoiceAsync(Guid id, CancellationToken ct = default) =>
        invoices.GetAsync(id, ct);

    public async Task<ApiResult<FeePaymentResponse>> PayInvoiceAsync(
        Guid id, PayFeeInvoiceRequest? req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeePaymentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var inv = await invoices.GetAsync(id, ct);
        if (inv is null)
            return ApiResult<FeePaymentResponse>.Fail(new Error("not_found", "resource not found"), 404);

        var alreadyPaid = inv.PaidAmount;
        var remaining = Math.Max(0, inv.Amount - alreadyPaid);
        if (remaining <= 0 || string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return ApiResult<FeePaymentResponse>.Fail(new Error("conflict", "invoice already paid"), 409);

        string method;
        decimal amount;
        string? paymentRef = req?.Ref;

        if (req?.Amount is { } a && a > 0)
        {
            amount = a;
            method = FirstNonEmpty(req.Method, req.Mode, "Cash")!;
        }
        else
        {
            /* Legacy / gateway path: charge remaining balance when body omits amount. */
            var result = await gateway.ChargeAsync(remaining, "INR");
            if (!result.Success)
                return ApiResult<FeePaymentResponse>.Fail(new Error("conflict", "payment failed"), 409);
            amount = remaining;
            method = result.Method ?? "upi_autopay";
            paymentRef ??= result.Reference;
        }

        if (amount > remaining)
            return ApiResult<FeePaymentResponse>.Fail(
                new Error("validation_error", $"Amount exceeds outstanding due ({remaining:0.##})"), 400);

        var classLabel = FirstNonEmpty(req?.ClassLabel, req?.Cls, inv.ClassLabel);
        var studentName = FirstNonEmpty(req?.StudentName, inv.StudentName);
        var feeType = FirstNonEmpty(req?.FeeType, req?.HeadName, "academic") ?? "academic";

        var payment = await invoices.RecordInvoicePaymentAsync(
            tid,
            id,
            new CreateFeePaymentRequest(
                inv.StudentId, studentName, classLabel, feeType, amount, method, paymentRef,
                InvoiceId: id, HeadId: req?.HeadId),
            amount,
            method,
            ct);
        if (payment is null)
            return ApiResult<FeePaymentResponse>.Fail(new Error("conflict", "invoice already paid"), 409);

        await live.PublishAsync(tid, LiveEventTypes.Fees, ct: ct);
        return ApiResult<FeePaymentResponse>.Ok(payment);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    public async Task<ApiResult<IReadOnlyList<FeeHeadResponse>>> ListHeadsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeeHeadResponse>>.Ok(await heads.ListAsync(ct));

    public async Task<ApiResult<FeeHeadResponse>> CreateHeadAsync(CreateFeeHeadRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeHeadResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiResult<FeeHeadResponse>.Fail(new Error("validation_error", "Fee type name is required"), 400);
        if (req.Name.Trim().Length > 120)
            return ApiResult<FeeHeadResponse>.Fail(new Error("validation_error", "Fee type name is too long"), 400);
        try
        {
            var created = await heads.CreateAsync(tid, req, ct);
            return ApiResult<FeeHeadResponse>.Ok(created!, 201);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return ApiResult<FeeHeadResponse>.Fail(
                new Error("conflict", $"{req.Name.Trim()} is already a fee type"), 409);
        }
    }

    public async Task<ApiResult<FeeHeadResponse>> UpdateHeadAsync(Guid id, UpdateFeeHeadRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeHeadResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (req.Name is not null && string.IsNullOrWhiteSpace(req.Name))
            return ApiResult<FeeHeadResponse>.Fail(new Error("validation_error", "Fee type name is required"), 400);
        try
        {
            var updated = await heads.UpdateAsync(id, tid, req, ct);
            return updated is null
                ? ApiResult<FeeHeadResponse>.Fail(new Error("not_found", "resource not found"), 404)
                : ApiResult<FeeHeadResponse>.Ok(updated);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return ApiResult<FeeHeadResponse>.Fail(
                new Error("conflict", $"{req.Name?.Trim()} is already a fee type"), 409);
        }
    }

    public async Task<ApiResult> DeleteHeadAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await heads.DeleteAsync(id, tid, ct))
            return ApiResult.Fail(new Error("not_found", "resource not found"), 404);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<FeeStructureResponse>> GetStructureAsync(CancellationToken ct = default)
    {
        var row = await structures.GetAsync(ct);
        return ApiResult<FeeStructureResponse>.Ok(row is null ? EmptyStructure() : ToResponse(row));
    }

    public async Task<ApiResult<IReadOnlyList<FeeStructureSummaryResponse>>> ListStructureHistoryAsync(CancellationToken ct = default)
    {
        var rows = await structures.ListHistoryAsync(ct);
        var students = await roster.ListAsync(null, null, null, null, ct);
        var studentCountByClass = students
            .GroupBy(ClassKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var headNameById = (await heads.ListAsync(ct))
            .ToDictionary(h => h.Id.ToString(), h => h.Name, StringComparer.OrdinalIgnoreCase);
        return ApiResult<IReadOnlyList<FeeStructureSummaryResponse>>.Ok(
            rows.Select(r => ToSummary(r, studentCountByClass, headNameById)).ToList());
    }

    public async Task<ApiResult<FeeStructureResponse>> GetStructureByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await structures.GetByIdAsync(id, ct);
        return row is null
            ? ApiResult<FeeStructureResponse>.Fail(new Error("not_found", "Fee structure version not found"), 404)
            : ApiResult<FeeStructureResponse>.Ok(ToResponse(row));
    }

    /// <summary>Publishes this version (Status = active). Many versions can be Published at
    /// the same time — this never changes any other version's status. Invoice generation
    /// (<see cref="GenerateInvoicesAsync"/>) merges every currently-Published version's amounts
    /// together, so publishing a second, independent fee (e.g. Transport, then Exam) adds to
    /// what's billed instead of replacing it.</summary>
    public async Task<ApiResult<FeeStructurePublishResponse>> PublishStructureAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeStructurePublishResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var row = await structures.PublishAsync(tid, id, ct);
        return row is null || !row.Found
            ? ApiResult<FeeStructurePublishResponse>.Fail(new Error("not_found", "Fee structure version not found"), 404)
            : ApiResult<FeeStructurePublishResponse>.Ok(new FeeStructurePublishResponse(row.Id!.Value, "active"));
    }

    /// <summary>Explicitly un-publishes this version (Status = inactive) — the only way a
    /// version stops being billed is a user clicking Unpublish; publishing another version
    /// never does this automatically.</summary>
    public async Task<ApiResult<FeeStructurePublishResponse>> UnpublishStructureAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeStructurePublishResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var row = await structures.UnpublishAsync(tid, id, ct);
        return row is null || !row.Found
            ? ApiResult<FeeStructurePublishResponse>.Fail(new Error("not_found", "Fee structure version not found"), 404)
            : ApiResult<FeeStructurePublishResponse>.Ok(new FeeStructurePublishResponse(row.Id!.Value, "inactive"));
    }

    /// <summary>Deep-merges two amounts matrices (class → headId → rate): every (class, head)
    /// pair from <paramref name="overlayJson"/> wins; every pair only present in
    /// <paramref name="baseJson"/> is carried forward unchanged. A class whose value is a bare
    /// number (the legacy flat-rate shape) is taken wholesale from whichever side has it —
    /// overlay wins if both do. Used to combine every currently-Published fee structure's
    /// amounts into one matrix for invoice generation, so no fee head is lost and the same
    /// (class, head) pair set by more than one Published version is never double-counted —
    /// the most-recently-created version (applied last, as the overlay) wins.</summary>
    private static string MergeAmountsJson(string? baseJson, string? overlayJson)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        void Apply(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                foreach (var classProp in doc.RootElement.EnumerateObject())
                {
                    if (classProp.Value.ValueKind == JsonValueKind.Number && classProp.Value.TryGetDecimal(out var flat))
                    {
                        merged[classProp.Name] = flat;
                        continue;
                    }
                    if (classProp.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!merged.TryGetValue(classProp.Name, out var existing) || existing is not Dictionary<string, decimal> headMap)
                    {
                        headMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                        merged[classProp.Name] = headMap;
                    }
                    foreach (var headProp in classProp.Value.EnumerateObject())
                    {
                        if (headProp.Value.ValueKind == JsonValueKind.Number && headProp.Value.TryGetDecimal(out var rate))
                            headMap[headProp.Name] = rate;
                    }
                }
            }
            catch (JsonException) { /* ignore malformed input, merge what we can */ }
        }

        Apply(baseJson);
        Apply(overlayJson);
        return JsonSerializer.Serialize(merged);
    }

    public async Task<ApiResult> DeleteStructureAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        var row = await structures.DeleteAsync(tid, id, ct);
        if (row is null || !row.Deleted)
        {
            return row?.Reason switch
            {
                "is_active" => ApiResult.Fail(new Error("conflict", "Cannot delete the currently published version — publish another version first."), 409),
                _ => ApiResult.Fail(new Error("not_found", "Fee structure version not found"), 404),
            };
        }
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<FeeStructureResponse>> UpsertStructureAsync(
        UpsertFeeStructureRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeStructureResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiResult<FeeStructureResponse>.Fail(new Error("validation_error", "Fee structure name is required"), 400);
        if (string.IsNullOrWhiteSpace(req.AcademicYear))
            return ApiResult<FeeStructureResponse>.Fail(new Error("validation_error", "Academic year is required"), 400);
        if (string.IsNullOrWhiteSpace(req.Currency))
            return ApiResult<FeeStructureResponse>.Fail(new Error("validation_error", "Currency is required"), 400);
        if (req.EffectiveFrom is null)
            return ApiResult<FeeStructureResponse>.Fail(new Error("validation_error", "Effective from date is required"), 400);

        var status = string.IsNullOrWhiteSpace(req.Status) ? "active" : req.Status.Trim().ToLowerInvariant();
        if (status is not ("active" or "inactive"))
            return ApiResult<FeeStructureResponse>.Fail(new Error("validation_error", "Status must be active or inactive"), 400);

        var amountsJson = SerializeAmounts(req.Amounts, req.AmountsJson);
        var saved = await structures.UpsertAsync(tid, req with { Status = status }, amountsJson, ct);
        return ApiResult<FeeStructureResponse>.Ok(ToResponse(saved!));
    }

    public async Task<ApiResult<GenerateFeeInvoicesResponse>> GenerateInvoicesAsync(
        GenerateFeeInvoicesRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<GenerateFeeInvoicesResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var year = req.AcademicYear?.Trim();
        var term = req.Term?.Trim();
        if (string.IsNullOrWhiteSpace(year) || string.IsNullOrWhiteSpace(term))
            return ApiResult<GenerateFeeInvoicesResponse>.Fail(
                new Error("validation_error", "academic_year and term are required"), 400);

        var classes = NormKeys(req.Classes);
        var grades = NormKeys(req.Grades);
        if (classes.Count == 0 && grades.Count == 0)
            return ApiResult<GenerateFeeInvoicesResponse>.Fail(
                new Error("validation_error", "Select at least one class or grade"), 400);

        // Every currently-Published fee structure applies — there is no single "live" row.
        // Merge them all together (oldest first, so the most recently created one wins any
        // (class, head) pair more than one of them sets) rather than reading just one.
        var activeStructures = await structures.ListActiveAsync(tid, ct);
        if (activeStructures.Count == 0)
            return ApiResult<GenerateFeeInvoicesResponse>.Fail(
                new Error("not_found", "No fee structure saved"), 404);

        var combinedAmountsJson = activeStructures
            .Select(s => s.AmountsJson)
            .Aggregate((string?)null, (merged, next) => merged is null ? next : MergeAmountsJson(merged, next));

        JsonElement amounts;
        try
        {
            amounts = string.IsNullOrWhiteSpace(combinedAmountsJson)
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonDocument.Parse(combinedAmountsJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            amounts = JsonDocument.Parse("{}").RootElement.Clone();
        }

        // A transport-flagged fee head only applies to a student whose active transport
        // assignment carries that exact FeeHeadId (StudentBusAssignments.FeeHeadId) — every other
        // fee head still applies to everyone, unchanged. Both lookups are bulk/tenant-wide so this
        // loop never issues a per-student query.
        var allHeads = await heads.ListAsync(ct);
        var transportHeadIds = allHeads
            .Where(h => h.IsTransportFeeHead)
            .Select(h => h.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headNameById = allHeads
            .ToDictionary(h => h.Id.ToString(), h => h.Name, StringComparer.OrdinalIgnoreCase);
        var headDescriptionById = allHeads
            .ToDictionary(h => h.Id.ToString(), h => h.Description, StringComparer.OrdinalIgnoreCase);
        var transportAssignments = (await studentBus.ListActiveFeeHeadIdsAsync(ct))
            .ToDictionary(r => r.StudentId, r => r.FeeHeadId);

        var students = await roster.ListAsync(null, null, null, null, ct);
        var period = $"{year} {term}";
        var created = 0;
        foreach (var student in students)
        {
            var label = ClassKey(student);
            var grade = (student.Grade ?? "").Trim();
            var matched = classes.Count > 0
                ? classes.Contains(label)
                : grades.Contains(grade);
            if (!matched) continue;

            var studentTransportFeeHeadId = transportAssignments.TryGetValue(student.Id, out var fh) ? fh : (Guid?)null;
            var (amount, lines) = AmountForWithLines(amounts, label, grade, transportHeadIds, studentTransportFeeHeadId, headNameById, headDescriptionById);
            if (amount <= 0) continue;
            if (await invoices.ExistsForStudentPeriodAsync(student.Id, period, ct))
                continue;

            var row = await invoices.CreateWithLinesAsync(
                tid, student.Id, period, req.DueDate, lines, ct);
            if (row is not null) created++;
        }

        return ApiResult<GenerateFeeInvoicesResponse>.Ok(new GenerateFeeInvoicesResponse(created));
    }

    public async Task<ApiResult<FeeReportSummaryResponse>> GetReportSummaryAsync(CancellationToken ct = default)
    {
        var invoiceRows = await invoices.ListAsync(null, ct);
        var paymentRows = await payments.ListAsync(null, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var billedTerm = invoiceRows.Sum(i => i.Amount);
        var outstanding = invoiceRows.Sum(i => Math.Max(0, i.Amount - i.PaidAmount));
        var defaulters = invoiceRows
            .Where(i => string.Equals(i.Status, "due", StringComparison.OrdinalIgnoreCase)
                        && i.PaidAmount <= 0)
            .Select(i => i.StudentId)
            .Distinct()
            .Count();

        var paidInvoices = invoiceRows
            .Where(i => string.Equals(i.Status, "paid", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var collectedFromPayments = paymentRows.Sum(p => p.Amount);
        var collectedTodayFromPayments = paymentRows
            .Where(p => DateOnly.FromDateTime(p.Date) == today)
            .Sum(p => p.Amount);
        var collectedFromInvoicePaidAmount = invoiceRows.Sum(i => i.PaidAmount);
        var collectedTodayFromPaidInvoices = paidInvoices
            .Where(i => i.PaidOn is { } d && DateOnly.FromDateTime(d) == today)
            .Sum(i => i.PaidAmount > 0 ? i.PaidAmount : i.Amount);

        /* Prefer FeePayments when present (Record payment writes them); else invoice PaidAmount. */
        var usePayments = paymentRows.Count > 0;
        var collectedTerm = usePayments ? collectedFromPayments : collectedFromInvoicePaidAmount;
        var collectedToday = usePayments ? collectedTodayFromPayments : collectedTodayFromPaidInvoices;

        var pct = billedTerm > 0
            ? Math.Round(collectedTerm / billedTerm * 100m, 1, MidpointRounding.AwayFromZero)
            : 0m;

        var byClass = invoiceRows
            .GroupBy(i => string.IsNullOrWhiteSpace(i.ClassLabel) ? "—" : i.ClassLabel!.Trim())
            .Select(g =>
            {
                var billed = g.Sum(x => x.Amount);
                var collected = g.Sum(x => x.PaidAmount);
                var value = billed > 0
                    ? Math.Round(collected / billed * 100m, 1, MidpointRounding.AwayFromZero)
                    : 0m;
                return new FeeReportByClass(g.Key, value, g.Select(x => x.StudentId).Distinct().Count());
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .ToList();

        IReadOnlyList<FeeReportByMode> byMode;
        if (usePayments)
        {
            byMode = paymentRows
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Method) ? "Other" : p.Method!.Trim())
                .Select(g => new FeeReportByMode(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Value)
                .ToList();
        }
        else
        {
            byMode = paidInvoices
                .GroupBy(i => string.IsNullOrWhiteSpace(i.Method) ? "Other" : i.Method!.Trim())
                .Select(g => new FeeReportByMode(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Value)
                .ToList();
        }

        FeeReportLatestPayment? latest = null;
        /* Live cue: only a real FeePayment from today (never invent from full invoice amount). */
        var todayPayments = paymentRows
            .Where(p => DateOnly.FromDateTime(p.Date) == today && p.Amount > 0)
            .OrderByDescending(p => p.Date)
            .ThenByDescending(p => p.Id)
            .ToList();
        if (todayPayments.Count > 0)
        {
            var p = todayPayments[0];
            latest = new FeeReportLatestPayment(
                p.Id, p.StudentId, p.StudentName, p.ClassLabel, p.Amount, p.Method, p.Ref, p.Date, p.HeadId);
        }

        return ApiResult<FeeReportSummaryResponse>.Ok(new FeeReportSummaryResponse(
            collectedToday, collectedTerm, outstanding, defaulters, billedTerm, pct,
            byClass, byMode, latest));
    }

    private static FeeStructureResponse EmptyStructure()
    {
        var year = DefaultAcademicYear();
        return new FeeStructureResponse(
            Id: null,
            TenantId: null,
            Name: $"School fees {year}",
            AcademicYear: year,
            ClassGrade: null,
            Section: null,
            Currency: "INR",
            EffectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveTo: null,
            Status: "active",
            Description: null,
            Amounts: JsonDocument.Parse("{}").RootElement.Clone());
    }

    private static FeeStructureSummaryResponse ToSummary(
        FeeStructureListRow row, IReadOnlyDictionary<string, int> studentCountByClass,
        IReadOnlyDictionary<string, string> headNameById)
    {
        var (total, headAmounts) = ProjectedRevenue(row.AmountsJson, studentCountByClass, headNameById);
        return new(
            row.Id, row.Name, row.AcademicYear, row.ClassGrade, row.Section, row.Currency,
            DateOnly.FromDateTime(row.EffectiveFrom), row.EffectiveTo is { } et ? DateOnly.FromDateTime(et) : null,
            row.Status, row.Description, row.CreatedAt, total, headAmounts);
    }

    /// For each class in the amounts JSON, resolves each fee head's rate, multiplies by however
    /// many students are CURRENTLY enrolled in that class (same "rate × headcount" idea as the
    /// Structure tab's "Expected annual collection" figure), and accumulates both a grand total
    /// and a per-fee-head total (e.g. "Exam Fee — ₹8,000") across every class that charges it.
    /// This is a live estimate against today's roster, not the actual amount ever invoiced
    /// (that's only known once invoices are generated, and is fixed per invoice via
    /// FeeInvoiceLines).
    private static (decimal Total, List<FeeStructureHeadAmountResponse> HeadAmounts) ProjectedRevenue(
        string? amountsJson, IReadOnlyDictionary<string, int> studentCountByClass,
        IReadOnlyDictionary<string, string> headNameById)
    {
        if (string.IsNullOrWhiteSpace(amountsJson)) return (0, []);
        try
        {
            using var doc = JsonDocument.Parse(amountsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (0, []);
            decimal total = 0;
            var byHeadName = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var headIdByName = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
            var studentsByHeadName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var byClass in doc.RootElement.EnumerateObject())
            {
                var enrolled = studentCountByClass.TryGetValue(byClass.Name, out var n) ? n : 0;
                if (enrolled <= 0) continue;

                if (byClass.Value.ValueKind == JsonValueKind.Number && byClass.Value.TryGetDecimal(out var flat))
                {
                    total += flat * enrolled;
                    byHeadName["Fee"] = byHeadName.GetValueOrDefault("Fee") + flat * enrolled;
                    headIdByName["Fee"] = null;
                    studentsByHeadName["Fee"] = studentsByHeadName.GetValueOrDefault("Fee") + enrolled;
                    continue;
                }
                if (byClass.Value.ValueKind != JsonValueKind.Object) continue;

                foreach (var byHead in byClass.Value.EnumerateObject())
                {
                    if (byHead.Value.ValueKind != JsonValueKind.Number || !byHead.Value.TryGetDecimal(out var rate)) continue;
                    var revenue = rate * enrolled;
                    total += revenue;
                    var headId = Guid.TryParse(byHead.Name, out var g) ? g : (Guid?)null;
                    var headName = headNameById.TryGetValue(byHead.Name, out var nm)
                        ? nm
                        : headId is { } deletedId
                            ? $"Deleted fee head ({deletedId.ToString()[..8]})"
                            : byHead.Name;
                    byHeadName[headName] = byHeadName.GetValueOrDefault(headName) + revenue;
                    headIdByName[headName] = headId;
                    studentsByHeadName[headName] = studentsByHeadName.GetValueOrDefault(headName) + enrolled;
                }
            }

            var headAmounts = byHeadName
                .Select(kv =>
                {
                    var students = studentsByHeadName.GetValueOrDefault(kv.Key);
                    var perStudent = students > 0 ? kv.Value / students : 0;
                    return new FeeStructureHeadAmountResponse(headIdByName[kv.Key], kv.Key, kv.Value, perStudent);
                })
                .OrderByDescending(h => h.Amount)
                .ToList();
            return (total, headAmounts);
        }
        catch (JsonException)
        {
            return (0, []);
        }
    }

    private static FeeStructureResponse ToResponse(FeeStructureRow row)
    {
        JsonElement amounts;
        try
        {
            amounts = string.IsNullOrWhiteSpace(row.AmountsJson)
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonDocument.Parse(row.AmountsJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            amounts = JsonDocument.Parse("{}").RootElement.Clone();
        }

        return new FeeStructureResponse(
            row.Id,
            row.TenantId,
            row.Name,
            row.AcademicYear,
            row.ClassGrade,
            row.Section,
            row.Currency,
            DateOnly.FromDateTime(row.EffectiveFrom),
            row.EffectiveTo is { } to ? DateOnly.FromDateTime(to) : null,
            row.Status,
            row.Description,
            amounts);
    }

    private static string SerializeAmounts(JsonElement? amounts, string? amountsJson)
    {
        if (!string.IsNullOrWhiteSpace(amountsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(amountsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    return doc.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                /* fall through to Amounts */
            }
        }
        if (amounts is null || amounts.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "{}";
        if (amounts.Value.ValueKind != JsonValueKind.Object)
            return "{}";
        return amounts.Value.GetRawText();
    }

    private static HashSet<string> NormKeys(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ClassKey(StudentResponse student)
    {
        if (!string.IsNullOrWhiteSpace(student.ClassLabel))
            return student.ClassLabel.Trim();
        var grade = (student.Grade ?? "").Trim();
        var section = (student.Section ?? "").Trim();
        return string.IsNullOrEmpty(section) ? grade : $"{grade}-{section}";
    }

    /// Resolves the class's (or, failing that, the grade's) per-head amounts into the invoice
    /// lines this specific student should be charged — transport-gated per TrySumHeads — plus
    /// their sum. Every fee head keeps its own line so the invoice can later explain its total.
    private static (decimal Total, List<FeeInvoiceLineInput> Lines) AmountForWithLines(
        JsonElement amounts, string classLabel, string grade,
        IReadOnlySet<string> transportHeadIds, Guid? studentTransportFeeHeadId,
        IReadOnlyDictionary<string, string> headNameById,
        IReadOnlyDictionary<string, string?> headDescriptionById)
    {
        if (amounts.ValueKind != JsonValueKind.Object) return (0, []);
        if (TrySumHeads(amounts, classLabel, transportHeadIds, studentTransportFeeHeadId, headNameById, headDescriptionById, out var byClass, out var classLines)
            && byClass > 0)
            return (byClass, classLines);
        if (!string.IsNullOrWhiteSpace(grade)
            && TrySumHeads(amounts, grade, transportHeadIds, studentTransportFeeHeadId, headNameById, headDescriptionById, out var byGrade, out var gradeLines))
            return (byGrade, gradeLines);
        return (0, []);
    }

    /// Sums every fee head's amount for the given class/grade key, EXCEPT a transport-flagged
    /// head (transportHeadIds, by FeeHead.Id string) whose id doesn't match this specific
    /// student's own assigned transport fee head — that head is skipped for this student, exactly
    /// as if it weren't in the structure at all. Every non-transport head is unaffected.
    /// `lines` mirrors the amounts actually summed, one per contributing head — the JSON key is
    /// resolved to a real FeeHeadId + name when it is one, or kept as a literal label otherwise
    /// (covers the legacy free-text head-key structures already saved before this feature).
    private static bool TrySumHeads(
        JsonElement amounts, string key,
        IReadOnlySet<string> transportHeadIds, Guid? studentTransportFeeHeadId,
        IReadOnlyDictionary<string, string> headNameById,
        IReadOnlyDictionary<string, string?> headDescriptionById,
        out decimal total, out List<FeeInvoiceLineInput> lines)
    {
        total = 0;
        lines = [];
        foreach (var prop in amounts.EnumerateObject())
        {
            if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out total))
            {
                if (total > 0) lines.Add(new FeeInvoiceLineInput(null, "Fee", total));
                return true;
            }
            if (prop.Value.ValueKind != JsonValueKind.Object) return false;
            foreach (var head in prop.Value.EnumerateObject())
            {
                if (head.Value.ValueKind != JsonValueKind.Number || !head.Value.TryGetDecimal(out var n)) continue;
                if (transportHeadIds.Contains(head.Name) &&
                    !(studentTransportFeeHeadId is { } assigned && string.Equals(head.Name, assigned.ToString(), StringComparison.OrdinalIgnoreCase)))
                    continue;
                total += n;
                if (n <= 0) continue;
                var headId = Guid.TryParse(head.Name, out var g) ? g : (Guid?)null;
                var headName = headNameById.TryGetValue(head.Name, out var nm)
                    ? nm
                    : headId is { } deletedId
                        ? $"Deleted fee head ({deletedId.ToString()[..8]})"
                        : head.Name;
                var description = headDescriptionById.TryGetValue(head.Name, out var desc) ? desc : null;
                lines.Add(new FeeInvoiceLineInput(headId, headName, n, description));
            }
            return true;
        }
        return false;
    }

    private static string DefaultAcademicYear(DateTime? utc = null)
    {
        var d = utc ?? DateTime.UtcNow;
        return d.Month >= 4
            ? $"{d.Year}-{((d.Year + 1) % 100):D2}"
            : $"{d.Year - 1}-{(d.Year % 100):D2}";
    }

    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message ?? "";
            if (msg.Contains("UQ_FeeHeads_Tenant_Name", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("UNIQUE KEY", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("2627") || msg.Contains("2601"))
                return true;
        }
        return false;
    }
}
