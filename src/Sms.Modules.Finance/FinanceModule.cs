using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Finance;

public sealed record FeePaymentResponse(
    Guid Id, Guid TenantId, Guid StudentId, string? StudentName, string? ClassLabel, string FeeType,
    decimal Amount, string? Method, string? Ref, DateTime Date);

public sealed record CreateFeePaymentRequest(
    Guid StudentId, string? StudentName, string? ClassLabel, string? FeeType, decimal Amount, string? Method, string? Ref);

public sealed class FeeRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date]";

    public Task<FeePaymentResponse?> CreateAsync(Guid tenantId, CreateFeePaymentRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeePaymentResponse>("dbo.FeePayment_Create", new
        {
            TenantId = tenantId, r.StudentId, r.StudentName, r.ClassLabel, r.FeeType, r.Amount, r.Method, r.Ref
        }, ct);

    public Task<IReadOnlyList<FeePaymentResponse>> ListAsync(Guid? studentId, CancellationToken ct = default) =>
        QueryInlineAsync<FeePaymentResponse>(
            $"SELECT {Cols} FROM dbo.FeePayments WHERE (@studentId IS NULL OR StudentId = @studentId) ORDER BY [Date] DESC",
            new { studentId }, ct);
}

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<FeeRepository>();
        return services;
    }

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 2 finance: /v1/fees/payments. Tenant-scoped.
    public static IEndpointRouteBuilder MapFinanceModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapGet("/fees/payments", async (FeeRepository repo, [FromQuery(Name = "student_id")] Guid? studentId) =>
            Results.Ok(new CursorPage<FeePaymentResponse>(await repo.ListAsync(studentId), null)));

        g.MapPost("/fees/payments", async (CreateFeePaymentRequest req, FeeRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<FeePaymentResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        return app;
    }
}
