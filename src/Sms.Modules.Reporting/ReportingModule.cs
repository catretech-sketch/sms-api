using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Reporting.Contracts;
using Sms.Modules.Reporting.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Time;

namespace Sms.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<ReportingRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportingModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapGet("/dashboard/stats", async (ReportingRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<DashboardStatsResponse>(
                await repo.GetDashboardStatsAsync(clock.UtcNow))))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        return app;
    }
}
