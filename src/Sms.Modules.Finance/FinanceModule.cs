using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Payments;
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

// ---- Fee invoices (student/parent bills) ----
public sealed record FeeInvoiceResponse(
    Guid Id, Guid TenantId, Guid StudentId, string? Period, DateTime? DueDate, decimal Amount,
    string Status, DateTime? PaidOn, string? Method);

public sealed record CreateFeeInvoiceRequest(Guid StudentId, string? Period, DateTime? DueDate, decimal Amount);

public sealed class FeeInvoiceRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, StudentId, Period, DueDate, Amount, Status, PaidOn, Method";

    public Task<FeeInvoiceResponse?> CreateAsync(Guid tenantId, CreateFeeInvoiceRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeeInvoiceResponse>("dbo.FeeInvoice_Create",
            new { TenantId = tenantId, r.StudentId, r.Period, r.DueDate, r.Amount }, ct);

    public Task<FeeInvoiceResponse?> MarkPaidAsync(Guid id, string method, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeeInvoiceResponse>("dbo.FeeInvoice_MarkPaid", new { Id = id, Method = method }, ct);

    public async Task<FeeInvoiceResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<FeeInvoiceResponse>($"SELECT {Cols} FROM dbo.FeeInvoices WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<FeeInvoiceResponse>> ListAsync(Guid? studentId, CancellationToken ct = default) =>
        QueryInlineAsync<FeeInvoiceResponse>(
            $"SELECT {Cols} FROM dbo.FeeInvoices WHERE (@studentId IS NULL OR StudentId = @studentId) ORDER BY DueDate DESC",
            new { studentId }, ct);
}

// ---- Payslips (HR/payroll) ----
public sealed record PayslipResponse(
    Guid Id, Guid TenantId, Guid UserId, string? Month, int Year, decimal Gross, decimal Deductions, decimal Net, string Status);
public sealed record CreatePayslipRequest(Guid UserId, string? Month, int Year, decimal Gross, decimal Deductions, decimal Net);

public sealed class PayslipRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, UserId, Month, Year, Gross, Deductions, Net, Status";

    public Task<PayslipResponse?> CreateAsync(Guid tenantId, CreatePayslipRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<PayslipResponse>("dbo.Payslip_Create",
            new { TenantId = tenantId, r.UserId, r.Month, r.Year, r.Gross, r.Deductions, r.Net }, ct);

    public Task<IReadOnlyList<PayslipResponse>> ListAsync(Guid? userId, CancellationToken ct = default) =>
        QueryInlineAsync<PayslipResponse>(
            $"SELECT {Cols} FROM dbo.Payslips WHERE (@userId IS NULL OR UserId = @userId) ORDER BY Year DESC, Month DESC",
            new { userId }, ct);
}

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<FeeRepository>();
        services.AddScoped<FeeInvoiceRepository>();
        services.AddScoped<PayslipRepository>();
        return services;
    }

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    private static IResult Conflict(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("conflict", message)), statusCode: 409);

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

        // ---- Fee invoices + online payment (parent) ----
        g.MapGet("/fees/invoices", async (FeeInvoiceRepository repo, [FromQuery(Name = "student_id")] Guid? studentId) =>
            Results.Ok(new CursorPage<FeeInvoiceResponse>(await repo.ListAsync(studentId), null)));

        g.MapPost("/fees/invoices", async (CreateFeeInvoiceRequest req, FeeInvoiceRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<FeeInvoiceResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        g.MapPost("/fees/invoices/{id:guid}/pay",
            async (Guid id, FeeInvoiceRepository repo, IPaymentGateway gateway) =>
        {
            var inv = await repo.GetAsync(id);
            if (inv is null) return NotFound();
            if (inv.Status == "paid") return Conflict("invoice already paid");
            var result = await gateway.ChargeAsync(inv.Amount, "INR");
            if (!result.Success) return Conflict("payment failed");
            return Results.Ok(new DataEnvelope<FeeInvoiceResponse>((await repo.MarkPaidAsync(id, result.Method))!));
        });

        // ---- Payslips ----
        g.MapGet("/payslips", async (PayslipRepository repo, [FromQuery(Name = "user_id")] Guid? userId, ITenantContext tenant) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<PayslipResponse>>(await repo.ListAsync(userId ?? tenant.UserId))));

        g.MapPost("/payslips", async (CreatePayslipRequest req, PayslipRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<PayslipResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        return app;
    }
}
