using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Shared.Kernel.Tenancy;

public sealed class BillingStateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, ITenantPlan plan)
    {
        var path = http.Request.Path.Value ?? "";
        var code = BlockCode(plan.Status, http.Request.Method, tenant.IsPlatform, path);
        if (code == 0) { await next(http); return; }

        http.Response.StatusCode = code;
        http.Response.ContentType = "application/json";
        var err = ErrorFor(plan.Status, code);
        await http.Response.WriteAsJsonAsync(ErrorEnvelope.From(err));
    }

    internal static Error ErrorFor(string status, int code) =>
        code == 402
            ? new Error("payment_required", "Account past due — writes are disabled until payment.")
            : status.ToLowerInvariant() switch
            {
                "trial" => new Error("tenant_pending_activation",
                    "School is pending Catre activation. You can create the school and pay; full access starts after Catre activates the client."),
                "hold" => new Error("tenant_on_hold",
                    "School is on hold by Catre. Contact support or wait until hold is released."),
                "deactivated" => new Error("tenant_deactivated",
                    "School was deactivated by Catre. Contact support to reactivate."),
                _ => new Error("tenant_suspended", "Account suspended. Contact support."),
            };

    /// 0 = allow; otherwise the HTTP status to return. Pure for testing.
    public static int BlockCode(string status, string method, bool isPlatform, string path)
    {
        if (isPlatform) return 0;
        if (path.StartsWith("/v1/auth/", StringComparison.OrdinalIgnoreCase)) return 0;

        /* Owner portfolio / billing while school cannot use SIS yet. */
        if (IsOwnerPortfolioPath(path) && IsNonActiveBlocked(status))
            return 0;

        if (IsNonActiveBlocked(status))
            return StatusCodes.Status403Forbidden;

        if (string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase))
            return IsWrite(method) ? StatusCodes.Status402PaymentRequired : 0;

        return 0; // active / unknown
    }

    /// trial / hold / deactivated / suspended — school console APIs blocked until Catre restores active.
    public static bool IsNonActiveBlocked(string status) =>
        status.ToLowerInvariant() is "trial" or "hold" or "deactivated" or "suspended";

    /// Owner console + payment flows must work before Catre activates the school.
    public static bool IsOwnerPortfolioPath(string path) =>
        path.StartsWith("/v1/me/", StringComparison.OrdinalIgnoreCase);

    private static bool IsWrite(string method) =>
        method is not ("GET" or "HEAD" or "OPTIONS");
}
