using Microsoft.AspNetCore.Http;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, ITenantPlan plan,
        TenantPlanRepository planRepo)
    {
        var user = http.User;
        var isPlatform = user.FindFirst("is_platform")?.Value == "1";
        Guid? userId = Guid.TryParse(user.FindFirst("sub")?.Value, out var uid) ? uid : null;
        Guid? tokenTenant = Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tt) ? tt : null;

        Guid? headerTenant = Guid.TryParse(http.Request.Headers["X-Tenant-Id"].ToString(), out var ht) ? ht : null;

        if (!isPlatform && tokenTenant is { } a && headerTenant is { } b && a != b)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tid = headerTenant ?? tokenTenant;
        tenant.Set(tid, userId, isPlatform);

        // Load tier+status once per request for tenant callers (Tenants is not RLS-scoped).
        if (!isPlatform && tid is { } t)
        {
            var ts = await planRepo.GetAsync(t);
            plan.Set(t, ts?.Tier ?? "", ts?.Status ?? "");
        }

        await next(http);
    }
}
