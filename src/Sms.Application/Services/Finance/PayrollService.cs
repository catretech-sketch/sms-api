using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sms.Application.Common;
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Finance;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public sealed record PayrollRunDetail(
    string Period, int Year, string? Month, string Status,
    int StaffCount, decimal Gross, decimal Deductions, decimal Net,
    DateTime? RunAt, DateTime? ApprovedAt, IReadOnlyList<PayrollRunLineResponse> Lines);

public interface IPayrollService
{
    Task<ApiResult<IReadOnlyList<SalaryProfileResponse>>> ListSalaryProfilesAsync(CancellationToken ct = default);
    Task<ApiResult<SalaryProfileResponse>> UpsertSalaryProfileAsync(
        string personType, Guid personId, UpsertSalaryProfileRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<SalaryStructureResponse>>> ListSalaryStructuresAsync(CancellationToken ct = default);
    Task<ApiResult<SalaryStructureResponse>> UpsertSalaryStructureAsync(
        UpsertSalaryStructureRequest req, CancellationToken ct = default);
    Task<ApiResult<PayrollRunDetail>> GetRunAsync(string period, bool preview = false, CancellationToken ct = default);
    Task<ApiResult<PayrollRunDetail>> RunAsync(string period, CancellationToken ct = default);
    Task<ApiResult<PayrollRunDetail>> ApproveAsync(string period, CancellationToken ct = default);
    /// Backfill Payslips rows from approved payroll runs for the signed-in user (mobile payslip feed).
    Task RepublishApprovedPayslipsForUserAsync(CancellationToken ct = default);
}

public sealed partial class PayrollService(
    PayrollRepository payroll,
    PayslipRepository payslips,
    TeacherRepository teachers,
    StaffRepository staff,
    IUserProvisioningDao users,
    ITenantContext tenant,
    ITenantFeatureSet features) : IPayrollService
{
    private static readonly JsonSerializerOptions CamelJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private bool PayrollAllowed => FeatureGate.Allowed(tenant, features, FeatureCatalog.HrPayroll);

    [GeneratedRegex(@"^\d{4}-\d{2}$")]
    private static partial Regex PeriodRe();

    public async Task<ApiResult<IReadOnlyList<SalaryProfileResponse>>> ListSalaryProfilesAsync(CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<IReadOnlyList<SalaryProfileResponse>>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<SalaryProfileResponse>>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<IReadOnlyList<SalaryProfileResponse>>.Ok(await payroll.ListSalaryProfilesAsync(tid, ct));
    }

    public async Task<ApiResult<SalaryProfileResponse>> UpsertSalaryProfileAsync(
        string personType, Guid personId, UpsertSalaryProfileRequest req, CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<SalaryProfileResponse>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<SalaryProfileResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var type = NormType(personType);
        if (type is null)
            return ApiResult<SalaryProfileResponse>.Fail(new Error("invalid_request", "personType must be 'teacher', 'staff' or 'leadership'"), 400);
        var clean = req with
        {
            BasicSalary = Math.Max(0, req.BasicSalary),
            Hra = Math.Max(0, req.Hra),
            Allowances = Math.Max(0, req.Allowances),
            Epf = Math.Max(0, req.Epf),
            ProfTax = Math.Max(0, req.ProfTax),
            OtherDeductions = Math.Max(0, req.OtherDeductions),
        };
        return ApiResult<SalaryProfileResponse>.Ok((await payroll.UpsertSalaryProfileAsync(tid, type, personId, clean, ct))!);
    }

    public async Task<ApiResult<IReadOnlyList<SalaryStructureResponse>>> ListSalaryStructuresAsync(CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<IReadOnlyList<SalaryStructureResponse>>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<SalaryStructureResponse>>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<IReadOnlyList<SalaryStructureResponse>>.Ok(await payroll.ListSalaryStructuresAsync(tid, ct));
    }

    public async Task<ApiResult<SalaryStructureResponse>> UpsertSalaryStructureAsync(
        UpsertSalaryStructureRequest req, CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<SalaryStructureResponse>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<SalaryStructureResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var type = NormType(req.PersonType);
        if (type is null)
            return ApiResult<SalaryStructureResponse>.Fail(new Error("invalid_request", "personType must be 'teacher', 'staff' or 'leadership'"), 400);
        var roleKey = (req.RoleKey ?? "").Trim();
        if (roleKey.Length == 0)
            return ApiResult<SalaryStructureResponse>.Fail(new Error("invalid_request", "roleKey is required"), 400);
        var clean = req with
        {
            PersonType = type,
            RoleKey = roleKey,
            Basic = Math.Max(0, req.Basic),
            Hra = Math.Max(0, req.Hra),
            Allowances = Math.Max(0, req.Allowances),
            Epf = Math.Max(0, req.Epf),
            ProfTax = Math.Max(0, req.ProfTax),
            OtherDeductions = Math.Max(0, req.OtherDeductions),
        };
        return ApiResult<SalaryStructureResponse>.Ok((await payroll.UpsertSalaryStructureAsync(tid, clean, ct))!);
    }

    public async Task<ApiResult<PayrollRunDetail>> GetRunAsync(string period, bool preview = false, CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<PayrollRunDetail>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<PayrollRunDetail>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!PeriodRe().IsMatch(period))
            return ApiResult<PayrollRunDetail>.Fail(new Error("invalid_request", "period must be YYYY-MM"), 400);

        // Preview always re-prices from the current salary structures/profiles, ignoring any frozen run.
        var run = preview ? null : await payroll.GetRunAsync(tid, period, ct);
        if (run is not null)
        {
            var lines = await payroll.ListRunLinesAsync(tid, period, ct);
            return ApiResult<PayrollRunDetail>.Ok(new PayrollRunDetail(
                run.Period, run.Year, run.Month, run.Status, run.StaffCount, run.Gross, run.Deductions, run.Net,
                run.RunAt, run.ApprovedAt, lines));
        }

        // No run yet — return a live draft preview from current salary profiles.
        var draft = await ComputeLinesAsync(tid, ct);
        var (year, month) = ParsePeriod(period);
        return ApiResult<PayrollRunDetail>.Ok(new PayrollRunDetail(
            period, year, month, "draft", draft.Count,
            draft.Sum(l => l.Gross), draft.Sum(l => l.Deductions), draft.Sum(l => l.Net),
            null, null, draft));
    }

    public async Task<ApiResult<PayrollRunDetail>> RunAsync(string period, CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<PayrollRunDetail>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<PayrollRunDetail>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!PeriodRe().IsMatch(period))
            return ApiResult<PayrollRunDetail>.Fail(new Error("invalid_request", "period must be YYYY-MM"), 400);

        var lines = await ComputeLinesAsync(tid, ct);
        var (year, month) = ParsePeriod(period);
        var gross = lines.Sum(l => l.Gross);
        var ded = lines.Sum(l => l.Deductions);
        var net = lines.Sum(l => l.Net);
        var json = JsonSerializer.Serialize(lines, CamelJson);

        var run = await payroll.SaveRunAsync(tid, period, year, month, lines.Count, gross, ded, net, tenant.UserId, json, ct);
        if (run is null)
            return ApiResult<PayrollRunDetail>.Fail(new Error("internal_error", "failed to save payroll run"), 500);

        await PublishPayslipsAsync(tid, lines, year, month, "pending", ct);

        return ApiResult<PayrollRunDetail>.Ok(new PayrollRunDetail(
            run.Period, run.Year, run.Month, run.Status, run.StaffCount, run.Gross, run.Deductions, run.Net,
            run.RunAt, run.ApprovedAt, lines), 201);
    }

    public async Task<ApiResult<PayrollRunDetail>> ApproveAsync(string period, CancellationToken ct = default)
    {
        if (!PayrollAllowed) return FeatureGate.Locked<PayrollRunDetail>(FeatureCatalog.HrPayroll);
        if (tenant.TenantId is not { } tid)
            return ApiResult<PayrollRunDetail>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!PeriodRe().IsMatch(period))
            return ApiResult<PayrollRunDetail>.Fail(new Error("invalid_request", "period must be YYYY-MM"), 400);

        // Re-price from the current salary structures/profiles at approval time so the locked
        // figures always match what the approver sees on screen (no stale snapshot).
        var fresh = await ComputeLinesAsync(tid, ct);
        var (year, month) = ParsePeriod(period);
        var json = JsonSerializer.Serialize(fresh, CamelJson);
        var saved = await payroll.SaveRunAsync(
            tid, period, year, month, fresh.Count,
            fresh.Sum(l => l.Gross), fresh.Sum(l => l.Deductions), fresh.Sum(l => l.Net),
            tenant.UserId, json, ct);
        if (saved is null)
            return ApiResult<PayrollRunDetail>.Fail(new Error("internal_error", "failed to prepare payroll for approval"), 500);

        var run = await payroll.ApproveRunAsync(tid, period, tenant.UserId, ct);
        if (run is null)
            return ApiResult<PayrollRunDetail>.Fail(new Error("not_found", "run payroll before approving"), 400);

        await PublishPayslipsAsync(tid, fresh, year, month, "paid", ct);
        return ApiResult<PayrollRunDetail>.Ok(new PayrollRunDetail(
            run.Period, run.Year, run.Month, run.Status, run.StaffCount, run.Gross, run.Deductions, run.Net,
            run.RunAt, run.ApprovedAt, fresh));
    }

    public async Task RepublishApprovedPayslipsForUserAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid) return;

        foreach (var run in await payroll.ListApprovedRunsAsync(tid, ct))
        {
            var (year, month) = ParsePeriod(run.Period);
            var monthLabel = run.Month ?? month;
            var slipStatus = run.Status.Equals("approved", StringComparison.OrdinalIgnoreCase) ? "paid" : "pending";
            foreach (var line in await payroll.ListRunLinesAsync(tid, run.Period, ct))
            {
                if (line.Gross <= 0 && line.Net <= 0) continue;
                var payUserId = await ResolvePayUserIdAsync(tid, line, ct);
                if (payUserId != uid) continue;
                await payslips.PublishForUserAsync(
                    tid, uid, monthLabel, year,
                    line.Basic, line.Hra, line.Allowances, line.Epf, line.ProfTax, line.OtherDeductions,
                    line.Gross, line.Deductions, line.Net, slipStatus, ct);
            }
        }
    }

    private async Task PublishPayslipsAsync(
        Guid tid, IReadOnlyList<PayrollRunLineResponse> lines, int year, string month, string status, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            if (line.Gross <= 0 && line.Net <= 0) continue;
            var userId = await ResolvePayUserIdAsync(tid, line, ct);
            if (userId is null) continue;
            await payslips.PublishForUserAsync(
                tid, userId.Value, month, year,
                line.Basic, line.Hra, line.Allowances, line.Epf, line.ProfTax, line.OtherDeductions,
                line.Gross, line.Deductions, line.Net, status, ct);
        }
    }

    private async Task<Guid?> ResolvePayUserIdAsync(Guid tenantId, PayrollRunLineResponse line, CancellationToken ct)
    {
        var type = (line.PersonType ?? "").Trim().ToLowerInvariant();
        if (type == "teacher") return await teachers.ResolvePayUserIdAsync(tenantId, line.PersonId, ct);
        if (type == "staff") return await staff.ResolvePayUserIdAsync(tenantId, line.PersonId, ct);
        if (type == "leadership") return line.PersonId;
        return null;
    }

    private async Task<List<PayrollRunLineResponse>> ComputeLinesAsync(Guid tid, CancellationToken ct)
    {
        var profiles = (await payroll.ListSalaryProfilesAsync(tid, ct))
            .ToDictionary(p => (Type: (p.PersonType ?? "").ToLowerInvariant(), p.PersonId));
        var structures = (await payroll.ListSalaryStructuresAsync(tid, ct))
            .ToDictionary(s => (Type: (s.PersonType ?? "").ToLowerInvariant(), Role: (s.RoleKey ?? "").Trim().ToLowerInvariant()));

        var lines = new List<PayrollRunLineResponse>();

        foreach (var t in await teachers.ListAsync(null, null, null, ct))
        {
            profiles.TryGetValue(("teacher", t.Id), out var pr);
            lines.Add(BuildLine("teacher", t.Id, t.Name, t.Designation, t.Department, pr, structures));
        }

        foreach (var s in await staff.ListAsync(null, null, ct))
        {
            profiles.TryGetValue(("staff", s.Id), out var pr);
            lines.Add(BuildLine("staff", s.Id, s.Name, s.Role, s.Department, pr, structures));
        }

        // Leadership (Principal / Owner) are login users, not People records. Include them on
        // payroll only when a salary is actually defined (per-person profile or a leadership
        // structure for their role) so we never show ₹0 leadership lines by default.
        foreach (var u in await users.ListByTenantAsync(tid, ct))
        {
            var roleKey = LeadershipRole(u.Roles);
            if (roleKey is null) continue;

            profiles.TryGetValue(("leadership", u.Id), out var pr);
            var hasProfilePay = pr is not null && HasPay(pr);
            var hasStructPay = structures.TryGetValue(("leadership", roleKey.ToLowerInvariant()), out var st)
                && StructHasPay(st);
            if (!hasProfilePay && !hasStructPay) continue;

            lines.Add(BuildLine("leadership", u.Id, LeadershipName(u), roleKey, "Leadership", pr, structures));
        }

        return lines;
    }

    /// Owner takes precedence over Principal when a user holds both roles.
    private static string? LeadershipRole(string? rolesCsv)
    {
        var roles = (rolesCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.ToLowerInvariant())
            .ToHashSet();
        if (roles.Contains(Policies.SchoolOwner)) return "Owner";
        if (roles.Contains(Policies.Principal)) return "Principal";
        return null;
    }

    private static string LeadershipName(SchoolUserListRow u)
    {
        var email = (u.Email ?? "").Trim();
        if (email.Contains('@')) return email[..email.IndexOf('@')];
        var phone = (u.Phone ?? "").Trim();
        return phone.Length > 0 ? phone : "Leadership";
    }

    private static bool StructHasPay(SalaryStructureResponse s) =>
        s.Basic > 0 || s.Hra > 0 || s.Allowances > 0 || s.Epf > 0 || s.ProfTax > 0 || s.OtherDeductions > 0;

    private static PayrollRunLineResponse BuildLine(
        string type, Guid id, string name, string? role, string? dept, SalaryProfileResponse? profile,
        IReadOnlyDictionary<(string Type, string Role), SalaryStructureResponse> structures)
    {
        // Person-level salary wins; when the person has no explicit pay, fall back to the
        // salary-structure template for their role/designation.
        SalaryComponents c;
        if (profile is not null && HasPay(profile))
        {
            c = new SalaryComponents(profile.BasicSalary, profile.Hra, profile.Allowances,
                profile.Epf, profile.ProfTax, profile.OtherDeductions);
        }
        else if (role is not null && structures.TryGetValue((type, role.Trim().ToLowerInvariant()), out var st))
        {
            c = new SalaryComponents(st.Basic, st.Hra, st.Allowances, st.Epf, st.ProfTax, st.OtherDeductions);
        }
        else
        {
            c = new SalaryComponents(profile?.BasicSalary ?? 0m, profile?.Hra ?? 0m, profile?.Allowances ?? 0m,
                profile?.Epf ?? 0m, profile?.ProfTax ?? 0m, profile?.OtherDeductions ?? 0m);
        }

        var (gross, ded, net) = Compute(c);
        // EPF shown on the payslip is the effective figure used in the deduction (statutory 12% when 0).
        var epfEffective = c.Epf > 0 ? c.Epf : Math.Round(Math.Max(0, c.Basic) * 0.12m, 2, MidpointRounding.AwayFromZero);
        return new PayrollRunLineResponse(
            type, id, name, role, dept,
            Math.Max(0, c.Basic), Math.Max(0, c.Hra), Math.Max(0, c.Allowances),
            epfEffective, Math.Max(0, c.ProfTax), Math.Max(0, c.OtherDeductions),
            gross, ded, net);
    }

    private static bool HasPay(SalaryProfileResponse p) =>
        p.BasicSalary > 0 || p.Hra > 0 || p.Allowances > 0 || p.Epf > 0 || p.ProfTax > 0 || p.OtherDeductions > 0;

    public readonly record struct SalaryComponents(
        decimal Basic, decimal Hra, decimal Allowances, decimal Epf, decimal ProfTax, decimal OtherDeductions);

    /// Authoritative pay math (mirrors src/lib/payroll.ts):
    /// gross = basic + hra + allowances; deductions = epf (or 12% of basic when 0) + prof-tax + other; net = gross - deductions.
    public static (decimal Gross, decimal Deductions, decimal Net) Compute(SalaryComponents c)
    {
        var basic = Math.Max(0, c.Basic);
        var gross = basic + Math.Max(0, c.Hra) + Math.Max(0, c.Allowances);
        var epf = c.Epf > 0 ? c.Epf : Math.Round(basic * 0.12m, 2, MidpointRounding.AwayFromZero);
        var ded = epf + Math.Max(0, c.ProfTax) + Math.Max(0, c.OtherDeductions);
        if (ded > gross) ded = gross;
        return (gross, ded, gross - ded);
    }

    /// Back-compat overload for basic+epf only.
    public static (decimal Gross, decimal Deductions, decimal Net) Compute(decimal basic, decimal epf) =>
        Compute(new SalaryComponents(basic, 0, 0, epf, 0, 0));

    private static string? NormType(string? t)
    {
        var v = (t ?? "").Trim().ToLowerInvariant();
        return v is "teacher" or "staff" or "leadership" ? v : null;
    }

    private static (int Year, string Month) ParsePeriod(string period)
    {
        var year = int.Parse(period[..4], CultureInfo.InvariantCulture);
        var monthNum = int.Parse(period[5..7], CultureInfo.InvariantCulture);
        var monthName = monthNum is >= 1 and <= 12
            ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(monthNum)
            : period;
        return (year, monthName);
    }
}
