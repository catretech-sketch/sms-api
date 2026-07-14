using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Sms.Api.Endpoints.Auth;
using Sms.Api.Extensions;
using Sms.Migrations;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureSmsServices();

var conn = builder.Configuration.GetConnectionString("Sql");
var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    MigrationRunner.Run(conn!);
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        foreach (var (key, title) in Sms.Api.Swagger.ApiAudienceMap.Apps)
            c.SwaggerEndpoint($"/swagger/{key}/swagger.json", title);
    });
}

await PlatformAdminSeeder.RunAsync(app);
await Sms.Api.Metrics.MetricsSnapshotWriter.RunAsync(app);

app.UseSerilogRequestLogging();
app.UseCors("sms");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<BillingStateMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "ready" : "unavailable";
        await ctx.Response.WriteAsJsonAsync(new { status });
    }
});

app.Run();

public partial class Program { }
