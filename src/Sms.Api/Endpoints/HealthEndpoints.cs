namespace Sms.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        // Liveness: the process is up. Readiness (/health/ready) is a real DB probe mapped in Program.cs.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }
}
