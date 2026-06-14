using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Sms.Modules.Tenancy;

public static class ModuleEndpoints
{
    // Phase 1 fills this group with /v1/clients, /plans, /subscriptions, etc.
    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1");
        g.MapGet("/tenancy/_ping", () => Results.Ok(new { module = "tenancy", status = "scaffold" }));
        return app;
    }
}
