using Sms.Application.Common;
using Sms.Application.DTOs.Auth;
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Finance;
using Sms.Modules.Tenancy.Contracts;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Auth;

public interface IMeSchoolsService
{
    Task<IReadOnlyList<ClientResponse>> ListAsync(CancellationToken ct = default);
    Task<ApiResult<ClientResponse>> CreateAsync(CreateMySchoolRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<PlanResponse>> ListPublishedPlansAsync(CancellationToken ct = default);
    Task<ApiResult<TokenResponse>> SwitchTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<FeeSummaryResponse> FeeSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public sealed record FeeSchoolSummary(
    Guid TenantId, string Name, decimal Collected, decimal Outstanding, int PaymentCount, int InvoiceCount);

public sealed record FeeSummaryTotals(decimal Collected, decimal Outstanding, int PaymentCount, int InvoiceCount);

public sealed record FeeSummaryPeriod(DateOnly From, DateOnly To);

public sealed record FeeSummaryResponse(
    FeeSummaryPeriod Period,
    IReadOnlyList<FeeSchoolSummary> Schools,
    FeeSummaryTotals Totals);

public sealed record CreateMySchoolRequest(
    string Name, string Slug, string? Country, Guid PlanId,
    string? AdminName, string? AdminPhone, string? Address, int TrialDays = 14);

/// <summary>
/// School-owner portfolio: list/create schools for the signed-in founding owner
/// (same email across tenant user rows) without platform privileges.
/// </summary>
public sealed class MeSchoolsService(
    ITenantContext tenant,
    IAuthDao auth,
    IUserProvisioningDao provisioning,
    ClientRepository clients,
    PlanRepository plans,
    OnboardingRepository onboarding,
    FeeInvoiceRepository feeInvoices,
    IJwtTokenService jwt,
    IRefreshTokenStore tokens) : IMeSchoolsService
{
    public async Task<IReadOnlyList<ClientResponse>> ListAsync(CancellationToken ct = default)
    {
        if (tenant.IsPlatform)
            return (await clients.ListAsync(null, null, null, ct)).Select(r => r.ToResponse()).ToList();

        if (tenant.UserId is not { } uid)
            return [];

        var me = await auth.GetByIdAsync(uid, ct);
        if (me?.Email is null)
            return [];

        // Elevate for cross-tenant reads (Tenants is not RLS-scoped the same way under platform).
        tenant.Set(null, uid, isPlatform: true);
        try
        {
            var peers = await auth.ListByEmailAsync(me.Email, ct);
            var ids = peers
                .Where(u => u.TenantId is not null && !u.IsPlatform)
                .Select(u => u.TenantId!.Value)
                .Distinct()
                .ToList();
            if (ids.Count == 0 && me.TenantId is { } only)
                ids.Add(only);
            if (ids.Count == 0) return [];
            var rows = await clients.GetManyAsync(ids, ct);
            return rows.Select(r => r.ToResponse()).ToList();
        }
        finally
        {
            tenant.Set(me.TenantId, uid, isPlatform: false);
        }
    }

    public async Task<ApiResult<ClientResponse>> CreateAsync(CreateMySchoolRequest req, CancellationToken ct = default)
    {
        if (tenant.IsPlatform)
            return ApiResult<ClientResponse>.Fail(new Error("forbidden", "Platform operators must use POST /clients."), 403);
        if (tenant.UserId is not { } uid)
            return ApiResult<ClientResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);

        var roles = await auth.GetRolesAsync(uid, ct);
        if (!roles.Contains(Policies.SchoolOwner) && !roles.Contains(Policies.SchoolAdmin))
            return ApiResult<ClientResponse>.Fail(new Error("forbidden", "Only a school owner can add schools."), 403);

        var me = await auth.GetByIdAsync(uid, ct);
        if (me?.Email is null)
            return ApiResult<ClientResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Slug))
            return ApiResult<ClientResponse>.Fail(new Error("invalid_request", "name and slug are required"), 422);

        tenant.Set(null, uid, isPlatform: true);
        try
        {
            var create = new CreateClientRequest(
                req.Name.Trim(), req.Slug.Trim().ToLowerInvariant(), req.Country,
                req.AdminName ?? me.Email.Split('@')[0], me.Email, req.AdminPhone ?? me.Phone,
                req.PlanId, req.TrialDays <= 0 ? 14 : req.TrialDays, null, req.Address);

            var row = await clients.CreateAsync(create, ct);
            if (row is null)
                return ApiResult<ClientResponse>.Fail(new Error("internal_error", "could not create school"), 500);

            var newUserId = await provisioning.CreateUserAsync(
                row.Id, me.Email, req.AdminPhone ?? me.Phone, false, [Policies.SchoolOwner], ct);
            if (me.PasswordHash is not null)
                await auth.SetPasswordAsync(newUserId, me.PasswordHash, ct);

            await onboarding.CreateAsync(new CreateOnboardingRequest(
                row.Name, row.Slug, row.Csm, row.Mrr, "trial",
                create.AdminName, create.AdminEmail, create.AdminPhone, create.Address, row.Id), ct);

            return ApiResult<ClientResponse>.Ok(row.ToResponse(), 201);
        }
        finally
        {
            tenant.Set(me.TenantId, uid, isPlatform: false);
        }
    }

    public async Task<IReadOnlyList<PlanResponse>> ListPublishedPlansAsync(CancellationToken ct = default)
    {
        var uid = tenant.UserId;
        var wasPlatform = tenant.IsPlatform;
        var tid = tenant.TenantId;
        tenant.Set(null, uid, isPlatform: true);
        try
        {
            // Catre has used both "published" and "public" for live plans; exclude drafts only.
            var rows = await plans.ListAsync(null, null, ct);
            return rows
                .Where(r => IsLivePlanVisibility(r.Visibility))
                .Select(r => r.ToResponse())
                .ToList();
        }
        finally
        {
            tenant.Set(tid, uid, wasPlatform);
        }
    }

    private static bool IsLivePlanVisibility(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility)) return false;
        var v = visibility.Trim();
        return v.Equals("published", StringComparison.OrdinalIgnoreCase)
            || v.Equals("public", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ApiResult<TokenResponse>> SwitchTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenant.IsPlatform)
            return ApiResult<TokenResponse>.Fail(new Error("forbidden", "Platform sessions cannot switch school tenants."), 403);
        if (tenant.UserId is not { } uid)
            return ApiResult<TokenResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);

        var me = await auth.GetByIdAsync(uid, ct);
        if (me?.Email is null)
            return ApiResult<TokenResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);

        tenant.Set(null, uid, isPlatform: true);
        try
        {
            var target = await auth.GetByEmailAndTenantAsync(me.Email, tenantId, ct);
            if (target is null)
                return ApiResult<TokenResponse>.Fail(new Error("forbidden", "You do not own that school."), 403);

            var roles = await auth.GetRolesAsync(target.Id, ct);
            var access = jwt.IssueAccess(target.Id, target.TenantId, roles, target.IsPlatform);
            var refresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(target.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30), ct);
            return ApiResult<TokenResponse>.Ok(new TokenResponse(access, refresh));
        }
        finally
        {
            tenant.Set(me.TenantId, uid, isPlatform: false);
        }
    }

    public async Task<FeeSummaryResponse> FeeSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodFrom = from ?? new DateOnly(today.Year, today.Month, 1);
        var periodTo = to ?? today;
        if (periodTo < periodFrom)
            (periodFrom, periodTo) = (periodTo, periodFrom);

        var empty = new FeeSummaryResponse(
            new FeeSummaryPeriod(periodFrom, periodTo),
            [],
            new FeeSummaryTotals(0, 0, 0, 0));

        List<Guid> ids;
        if (tenant.IsPlatform)
        {
            ids = (await clients.ListAsync(null, null, null, ct)).Select(r => r.Id).ToList();
        }
        else
        {
            if (tenant.UserId is not { } uid)
                return empty;
            var me = await auth.GetByIdAsync(uid, ct);
            if (me?.Email is null)
                return empty;

            var prevTenant = me.TenantId;
            tenant.Set(null, uid, isPlatform: true);
            try
            {
                var peers = await auth.ListByEmailAsync(me.Email, ct);
                ids = peers
                    .Where(u => u.TenantId is not null && !u.IsPlatform)
                    .Select(u => u.TenantId!.Value)
                    .Distinct()
                    .ToList();
                if (ids.Count == 0 && me.TenantId is { } only)
                    ids.Add(only);
                if (ids.Count == 0)
                    return empty;

                var rows = await feeInvoices.SummarizeByTenantsAsync(ids, periodFrom, periodTo, ct);
                return ToFeeSummary(periodFrom, periodTo, rows);
            }
            finally
            {
                tenant.Set(prevTenant, uid, isPlatform: false);
            }
        }

        if (ids.Count == 0)
            return empty;
        var platformRows = await feeInvoices.SummarizeByTenantsAsync(ids, periodFrom, periodTo, ct);
        return ToFeeSummary(periodFrom, periodTo, platformRows);
    }

    private static FeeSummaryResponse ToFeeSummary(
        DateOnly from, DateOnly to, IReadOnlyList<FeeTenantSummaryRow> rows)
    {
        var schools = rows.Select(r => new FeeSchoolSummary(
            r.TenantId, r.Name, r.Collected, r.Outstanding, r.PaymentCount, r.InvoiceCount)).ToList();
        var totals = new FeeSummaryTotals(
            schools.Sum(s => s.Collected),
            schools.Sum(s => s.Outstanding),
            schools.Sum(s => s.PaymentCount),
            schools.Sum(s => s.InvoiceCount));
        return new FeeSummaryResponse(new FeeSummaryPeriod(from, to), schools, totals);
    }

    private static string Sha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
