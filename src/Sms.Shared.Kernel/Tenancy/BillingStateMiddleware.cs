using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class BillingStateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, ITenantPlan plan)
    {
        var code = BlockCode(plan.Status, http.Request.Method, tenant.IsPlatform,
            http.Request.Path.Value ?? "");
        if (code == 0) { await next(http); return; }

        http.Response.StatusCode = code;
        http.Response.ContentType = "application/json";
        var err = code == 402
            ? new Error("payment_required", "Account past due — writes are disabled until payment.")
            : new Error("tenant_suspended", "Account suspended. Contact support.");
        await http.Response.WriteAsJsonAsync(ErrorEnvelope.From(err));
    }

    /// 0 = allow; otherwise the HTTP status to return. Pure for testing.
    public static int BlockCode(string status, string method, bool isPlatform, string path)
    {
        if (isPlatform) return 0;
        if (path.StartsWith("/v1/auth/", StringComparison.OrdinalIgnoreCase)) return 0;

        if (string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase))
            return StatusCodes.Status403Forbidden;

        if (string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase))
            return IsWrite(method) ? StatusCodes.Status402PaymentRequired : 0;

        return 0; // active / trial / unknown
    }

    private static bool IsWrite(string method) =>
        method is not ("GET" or "HEAD" or "OPTIONS");
}
