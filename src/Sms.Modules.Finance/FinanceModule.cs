using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

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

    /// <summary>
    /// Cross-tenant fee rollup (call under platform elevation so RLS does not filter peers out).
    /// </summary>
    public Task<IReadOnlyList<FeeTenantSummaryRow>> SummarizeByTenantsAsync(
        IReadOnlyList<Guid> tenantIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (tenantIds.Count == 0)
            return Task.FromResult<IReadOnlyList<FeeTenantSummaryRow>>([]);
        var json = System.Text.Json.JsonSerializer.Serialize(tenantIds);
        return QueryProcAsync<FeeTenantSummaryRow>("dbo.Fee_SummaryByTenants",
            new { TenantIds = json, From = from.ToDateTime(TimeOnly.MinValue), To = to.ToDateTime(TimeOnly.MinValue) }, ct);
    }
}

public sealed record FeeTenantSummaryRow(
    Guid TenantId, string Name, decimal Collected, decimal Outstanding, int PaymentCount, int InvoiceCount);

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

// ---- Payroll (salary master + monthly run/approve) ----
public sealed record SalaryProfileResponse(
    Guid TenantId, string PersonType, Guid PersonId, decimal BasicSalary, decimal Hra, decimal Allowances,
    decimal Epf, decimal ProfTax, decimal OtherDeductions, string? Uan,
    string? BankHolder, string? BankAccount, string? BankName, string? Ifsc, string? BankBranch);

public sealed record UpsertSalaryProfileRequest(
    decimal BasicSalary, decimal Hra, decimal Allowances, decimal Epf, decimal ProfTax, decimal OtherDeductions,
    string? Uan, string? BankHolder, string? BankAccount, string? BankName, string? Ifsc, string? BankBranch);

// ---- Salary structure templates keyed by role/designation ----
public sealed record SalaryStructureResponse(
    Guid TenantId, string PersonType, string RoleKey,
    decimal Basic, decimal Hra, decimal Allowances, decimal Epf, decimal ProfTax, decimal OtherDeductions);

public sealed record UpsertSalaryStructureRequest(
    string PersonType, string RoleKey,
    decimal Basic, decimal Hra, decimal Allowances, decimal Epf, decimal ProfTax, decimal OtherDeductions);

public sealed record PayrollRunResponse(
    Guid Id, Guid TenantId, string Period, int Year, string? Month, string Status,
    int StaffCount, decimal Gross, decimal Deductions, decimal Net,
    Guid? RunBy, DateTime? RunAt, Guid? ApprovedBy, DateTime? ApprovedAt);

public sealed record PayrollRunLineResponse(
    string PersonType, Guid PersonId, string Name, string? Role, string? Dept,
    decimal Basic, decimal Hra, decimal Allowances, decimal Epf, decimal ProfTax, decimal OtherDeductions,
    decimal Gross, decimal Deductions, decimal Net);

public sealed class PayrollRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<SalaryProfileResponse?> UpsertSalaryProfileAsync(
        Guid tenantId, string personType, Guid personId, UpsertSalaryProfileRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<SalaryProfileResponse>("dbo.SalaryProfile_Upsert", new
        {
            TenantId = tenantId, PersonType = personType, PersonId = personId,
            r.BasicSalary, r.Hra, r.Allowances, r.Epf, r.ProfTax, r.OtherDeductions,
            r.Uan, r.BankHolder, r.BankAccount, r.BankName, r.Ifsc, r.BankBranch,
        }, ct);

    public Task<IReadOnlyList<SalaryProfileResponse>> ListSalaryProfilesAsync(Guid tenantId, CancellationToken ct = default) =>
        QueryProcAsync<SalaryProfileResponse>("dbo.SalaryProfile_List", new { TenantId = tenantId }, ct);

    public Task<SalaryStructureResponse?> UpsertSalaryStructureAsync(
        Guid tenantId, UpsertSalaryStructureRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<SalaryStructureResponse>("dbo.SalaryStructure_Upsert", new
        {
            TenantId = tenantId, r.PersonType, r.RoleKey,
            r.Basic, r.Hra, r.Allowances, r.Epf, r.ProfTax, r.OtherDeductions,
        }, ct);

    public Task<IReadOnlyList<SalaryStructureResponse>> ListSalaryStructuresAsync(Guid tenantId, CancellationToken ct = default) =>
        QueryProcAsync<SalaryStructureResponse>("dbo.SalaryStructure_List", new { TenantId = tenantId }, ct);

    public Task<PayrollRunResponse?> GetRunAsync(Guid tenantId, string period, CancellationToken ct = default) =>
        QuerySingleProcAsync<PayrollRunResponse>("dbo.PayrollRun_Get", new { TenantId = tenantId, Period = period }, ct);

    public Task<IReadOnlyList<PayrollRunLineResponse>> ListRunLinesAsync(Guid tenantId, string period, CancellationToken ct = default) =>
        QueryProcAsync<PayrollRunLineResponse>("dbo.PayrollRunLine_ListByPeriod", new { TenantId = tenantId, Period = period }, ct);

    public Task<PayrollRunResponse?> SaveRunAsync(
        Guid tenantId, string period, int year, string? month, int staffCount,
        decimal gross, decimal deductions, decimal net, Guid? runBy, string linesJson, CancellationToken ct = default) =>
        QuerySingleProcAsync<PayrollRunResponse>("dbo.PayrollRun_Save", new
        {
            TenantId = tenantId, Period = period, Year = year, Month = month, StaffCount = staffCount,
            Gross = gross, Deductions = deductions, Net = net, RunBy = runBy, Lines = linesJson,
        }, ct);

    public Task<PayrollRunResponse?> ApproveRunAsync(Guid tenantId, string period, Guid? approvedBy, CancellationToken ct = default) =>
        QuerySingleProcAsync<PayrollRunResponse>("dbo.PayrollRun_Approve",
            new { TenantId = tenantId, Period = period, ApprovedBy = approvedBy }, ct);
}

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<FeeRepository>();
        services.AddScoped<FeeInvoiceRepository>();
        services.AddScoped<PayslipRepository>();
        services.AddScoped<PayrollRepository>();
        return services;
    }
}
