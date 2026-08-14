using System.Text.Json;
using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public interface IFeeService
{
    Task<ApiResult<IReadOnlyList<FeePaymentResponse>>> ListPaymentsAsync(Guid? studentId, CancellationToken ct = default);
    Task<ApiResult<FeePaymentResponse>> CreatePaymentAsync(CreateFeePaymentRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FeeInvoiceResponse>>> ListInvoicesAsync(Guid? studentId, CancellationToken ct = default);
    Task<ApiResult<FeeInvoiceResponse>> CreateInvoiceAsync(CreateFeeInvoiceRequest req, CancellationToken ct = default);
    Task<ApiResult<FeePaymentResponse>> PayInvoiceAsync(Guid id, PayFeeInvoiceRequest? req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<FeeHeadResponse>>> ListHeadsAsync(CancellationToken ct = default);
    Task<ApiResult<FeeHeadResponse>> CreateHeadAsync(CreateFeeHeadRequest req, CancellationToken ct = default);
    Task<ApiResult<FeeHeadResponse>> UpdateHeadAsync(Guid id, UpdateFeeHeadRequest req, CancellationToken ct = default);
    Task<ApiResult> DeleteHeadAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<FeeStructureResponse>> GetStructureAsync(CancellationToken ct = default);
    Task<ApiResult<FeeStructureResponse>> UpsertStructureAsync(UpsertFeeStructureRequest req, CancellationToken ct = default);
    Task<ApiResult<FeeReportSummaryResponse>> GetReportSummaryAsync(CancellationToken ct = default);
}

public sealed class FeeService(
    FeeRepository payments,
    FeeInvoiceRepository invoices,
    FeeHeadRepository heads,
    FeeStructureRepository structures,
    IPaymentGateway gateway,
    ITenantContext tenant) : IFeeService
{
    public async Task<ApiResult<IReadOnlyList<FeePaymentResponse>>> ListPaymentsAsync(Guid? studentId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeePaymentResponse>>.Ok(await payments.ListAsync(studentId, ct));

    public async Task<ApiResult<FeePaymentResponse>> CreatePaymentAsync(CreateFeePaymentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeePaymentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<FeePaymentResponse>.Ok((await payments.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<FeeInvoiceResponse>>> ListInvoicesAsync(Guid? studentId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeeInvoiceResponse>>.Ok(await invoices.ListAsync(studentId, ct));

    public async Task<ApiResult<FeeInvoiceResponse>> CreateInvoiceAsync(CreateFeeInvoiceRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeInvoiceResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<FeeInvoiceResponse>.Ok((await invoices.CreateAsync(tid, req, ct))!, 201);
    }

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

        var payment = await payments.CreateAsync(tid, new CreateFeePaymentRequest(
            inv.StudentId, studentName, classLabel, feeType, amount, method, paymentRef), ct);
        if (payment is null)
            return ApiResult<FeePaymentResponse>.Fail(new Error("internal_error", "payment not recorded"), 500);

        var updated = await invoices.ApplyPaymentAsync(id, amount, method, ct);
        if (updated is null)
            return ApiResult<FeePaymentResponse>.Fail(new Error("internal_error", "invoice not updated"), 500);

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

        var amountsJson = SerializeAmounts(req.Amounts);
        var saved = await structures.UpsertAsync(tid, req with { Status = status }, amountsJson, ct);
        return ApiResult<FeeStructureResponse>.Ok(ToResponse(saved!));
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
                p.Id, p.StudentId, p.StudentName, p.ClassLabel, p.Amount, p.Method, p.Ref, p.Date);
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

    private static string SerializeAmounts(JsonElement? amounts)
    {
        if (amounts is null || amounts.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "{}";
        if (amounts.Value.ValueKind != JsonValueKind.Object)
            return "{}";
        return amounts.Value.GetRawText();
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
