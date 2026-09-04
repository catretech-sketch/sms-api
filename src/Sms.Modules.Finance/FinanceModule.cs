using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Audit;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Finance;

public sealed record FeePaymentResponse(
    Guid Id, Guid TenantId, Guid StudentId, string? StudentName, string? ClassLabel, string FeeType,
    decimal Amount, string? Method, string? Ref, DateTime Date,
    Guid? InvoiceId = null, string? HeadId = null);

public sealed record CreateFeePaymentRequest(
    Guid StudentId, string? StudentName, string? ClassLabel, string? FeeType, decimal Amount, string? Method, string? Ref,
    Guid? InvoiceId = null, string? HeadId = null, string? HeadName = null, string? Mode = null, string? Cls = null,
    Guid? IdempotencyKey = null);

public sealed class FeeRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId";

    public Task<FeePaymentResponse?> CreateAsync(Guid tenantId, CreateFeePaymentRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeePaymentResponse>("dbo.FeePayment_Create", new
        {
            TenantId = tenantId,
            r.StudentId,
            StudentName = r.StudentName,
            ClassLabel = string.IsNullOrWhiteSpace(r.ClassLabel) ? r.Cls : r.ClassLabel,
            FeeType = string.IsNullOrWhiteSpace(r.FeeType) ? r.HeadName : r.FeeType,
            r.Amount,
            Method = string.IsNullOrWhiteSpace(r.Method) ? r.Mode : r.Method,
            r.Ref,
            r.InvoiceId,
            r.HeadId,
        }, ct);

    public Task<IReadOnlyList<FeePaymentResponse>> ListAsync(Guid? studentId, CancellationToken ct = default) =>
        QueryInlineAsync<FeePaymentResponse>(
            $"SELECT {Cols} FROM dbo.FeePayments WHERE (@studentId IS NULL OR StudentId = @studentId) ORDER BY [Date] DESC",
            new { studentId }, ct);

    public async Task<FeePaymentResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<FeePaymentResponse>(
            $"SELECT {Cols} FROM dbo.FeePayments WHERE Id = @id", new { id }, ct)).FirstOrDefault();
}

// ---- Fee invoices (student/parent bills) ----
public sealed record FeeInvoiceResponse(
    Guid Id, Guid TenantId, Guid StudentId, string? Period, DateTime? DueDate, decimal Amount,
    string Status, DateTime? PaidOn, string? Method,
    string? StudentName = null, string? ClassLabel = null, string? AdmissionNo = null,
    string? Grade = null, int? AvatarHue = null, string? PhotoUrl = null,
    decimal PaidAmount = 0);

public sealed record CreateFeeInvoiceRequest(Guid StudentId, string? Period, DateTime? DueDate, decimal Amount);

/// <summary>Body for POST /fees/invoices/{id}/pay (CRM Record payment).</summary>
public sealed record PayFeeInvoiceRequest(
    decimal? Amount,
    string? Method,
    string? Mode,
    string? Ref,
    string? StudentName,
    string? ClassLabel,
    string? Cls,
    string? FeeType,
    string? HeadId,
    string? HeadName,
    Guid? IdempotencyKey = null);

public sealed class FeeInvoiceRepository(IDbConnectionFactory factory, IAuditLogger auditLogger) : BaseRepository(factory)
{
    private static int _paidAmountReady;

    private const string InvoiceCols =
        "i.Id, i.TenantId, i.StudentId, i.Period, i.DueDate, i.Amount, i.Status, i.PaidOn, i.Method, ISNULL(i.PaidAmount, 0) AS PaidAmount";
    private const string StudentJoinCols =
        "s.Name AS StudentName, s.ClassLabel, s.AdmissionNo, s.Grade, s.AvatarHue, s.PhotoUrl";
    private const string SelectJoined =
        $"SELECT {InvoiceCols}, {StudentJoinCols} FROM dbo.FeeInvoices i LEFT JOIN dbo.Students s ON s.Id = i.StudentId";

    private async Task EnsurePaidAmountColumnAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _paidAmountReady, 1, 0) != 0) return;
        try
        {
            await ExecuteInlineAsync(
                """
                IF COL_LENGTH('dbo.FeeInvoices', 'PaidAmount') IS NULL
                    ALTER TABLE dbo.FeeInvoices ADD PaidAmount decimal(18,2) NOT NULL
                        CONSTRAINT DF_FeeInvoices_PaidAmount DEFAULT (0);
                """,
                null, ct);

            /* Reopen phantom "paid" with PaidAmount 0 (false full-pay from earlier bug). */
            await ExecuteInlineAsync(
                """
                ;WITH pay AS (
                    SELECT StudentId, CAST(SUM(Amount) AS decimal(18,2)) AS Paid
                    FROM dbo.FeePayments
                    GROUP BY StudentId
                )
                UPDATE i
                SET
                    PaidAmount = CASE
                        WHEN ISNULL(p.Paid, 0) > i.Amount THEN i.Amount
                        ELSE ISNULL(p.Paid, 0)
                    END,
                    Status = CASE
                        WHEN ISNULL(p.Paid, 0) >= i.Amount AND i.Amount > 0 THEN N'paid'
                        WHEN ISNULL(p.Paid, 0) > 0 THEN N'partial'
                        ELSE N'due'
                    END,
                    PaidOn = CASE
                        WHEN ISNULL(p.Paid, 0) >= i.Amount AND i.Amount > 0
                            THEN ISNULL(i.PaidOn, CAST(SYSUTCDATETIME() AS date))
                        ELSE NULL
                    END,
                    Method = CASE WHEN ISNULL(p.Paid, 0) > 0 THEN i.Method ELSE NULL END
                FROM dbo.FeeInvoices i
                LEFT JOIN pay p ON p.StudentId = i.StudentId
                WHERE i.Status = N'paid'
                  AND ISNULL(i.PaidAmount, 0) = 0
                  AND i.Amount > 0;
                """,
                null, ct);
        }
        catch
        {
            Interlocked.Exchange(ref _paidAmountReady, 0);
            throw;
        }
    }

    public async Task<FeeInvoiceResponse?> CreateAsync(Guid tenantId, CreateFeeInvoiceRequest r, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        var core = await QuerySingleProcAsync<FeeInvoiceCore>("dbo.FeeInvoice_Create",
            new { TenantId = tenantId, r.StudentId, r.Period, r.DueDate, r.Amount }, ct);
        return core is null ? null : await GetAsync(core.Id, ct);
    }

    public async Task<FeeInvoiceResponse?> MarkPaidAsync(Guid id, string method, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        var core = await QuerySingleProcAsync<FeeInvoiceCore>("dbo.FeeInvoice_MarkPaid", new { Id = id, Method = method }, ct);
        if (core is null) return null;
        await ExecuteInlineAsync(
            "UPDATE dbo.FeeInvoices SET PaidAmount = Amount WHERE Id = @id",
            new { id }, ct);
        return await GetAsync(core.Id, ct);
    }

    public async Task<FeeInvoiceResponse?> MarkStatusAsync(
        Guid id, string status, string? method, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        await ExecuteInlineAsync(
            """
            UPDATE dbo.FeeInvoices
            SET Status = @status,
                Method = COALESCE(@method, Method),
                PaidOn = CASE WHEN @status = N'paid' THEN CAST(SYSUTCDATETIME() AS date) ELSE PaidOn END
            WHERE Id = @id AND Status <> N'paid'
            """,
            new { id, status, method }, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Add a payment toward the invoice; sets status paid/partial from accumulated PaidAmount.</summary>
    public async Task<FeeInvoiceResponse?> ApplyPaymentAsync(
        Guid id, decimal amount, string method, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        await ExecuteInlineAsync(
            """
            UPDATE dbo.FeeInvoices
            SET
                PaidAmount = ISNULL(PaidAmount, 0) + @amount,
                Method = @method,
                Status = CASE
                    WHEN ISNULL(PaidAmount, 0) + @amount >= Amount THEN N'paid'
                    ELSE N'partial'
                END,
                PaidOn = CASE
                    WHEN ISNULL(PaidAmount, 0) + @amount >= Amount THEN CAST(SYSUTCDATETIME() AS date)
                    ELSE PaidOn
                END
            WHERE Id = @id
              AND Status <> N'paid'
            """,
            new { id, amount, method }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<FeePaymentResponse?> RecordInvoicePaymentAsync(
        Guid tenantId, Guid invoiceId, CreateFeePaymentRequest req, decimal amount, string method,
        Guid? actorUserId, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        await using var conn = await Factory.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            if (req.IdempotencyKey is { } key)
            {
                var existing = await conn.QuerySingleOrDefaultAsync<FeePaymentResponse>(new CommandDefinition(
                    """
                    SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
                    FROM dbo.FeePayments WHERE TenantId = @tenantId AND IdempotencyKey = @key
                    """,
                    new { tenantId, key }, tx, cancellationToken: ct));
                if (existing is not null)
                {
                    await tx.CommitAsync(ct);
                    return existing;
                }
            }

            var inv = await conn.QuerySingleOrDefaultAsync<InvoiceLockRow>(
                new CommandDefinition(
                    """
                    SELECT Id, StudentId, Amount, Status, ISNULL(PaidAmount, 0) AS PaidAmount
                    FROM dbo.FeeInvoices WITH (UPDLOCK, HOLDLOCK)
                    WHERE Id = @invoiceId
                    """,
                    new { invoiceId }, tx, cancellationToken: ct));
            if (inv is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var remaining = Math.Max(0, inv.Amount - inv.PaidAmount);
            if (remaining <= 0 || string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var payId = Guid.NewGuid();
            var classLabel = string.IsNullOrWhiteSpace(req.ClassLabel) ? req.Cls : req.ClassLabel;
            var feeType = string.IsNullOrWhiteSpace(req.FeeType)
                ? (string.IsNullOrWhiteSpace(req.HeadName) ? "academic" : req.HeadName)
                : req.FeeType;
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.FeePayments (Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId, IdempotencyKey, CreatedAt)
                VALUES (@payId, @tenantId, @StudentId, @StudentName, @classLabel, @feeType, @amount, @method, @Ref, CAST(SYSUTCDATETIME() AS date), @invoiceId, @HeadId, @IdempotencyKey, SYSUTCDATETIME())
                """,
                new
                {
                    payId,
                    tenantId,
                    inv.StudentId,
                    req.StudentName,
                    classLabel,
                    feeType,
                    amount,
                    method,
                    req.Ref,
                    invoiceId,
                    req.HeadId,
                    req.IdempotencyKey,
                }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.FeeInvoices
                SET
                    PaidAmount = ISNULL(PaidAmount, 0) + @amount,
                    Method = @method,
                    Status = CASE
                        WHEN ISNULL(PaidAmount, 0) + @amount >= Amount THEN N'paid'
                        ELSE N'partial'
                    END,
                    PaidOn = CASE
                        WHEN ISNULL(PaidAmount, 0) + @amount >= Amount THEN CAST(SYSUTCDATETIME() AS date)
                        ELSE PaidOn
                    END
                WHERE Id = @invoiceId
                  AND Status <> N'paid'
                """,
                new { invoiceId, amount, method }, tx, cancellationToken: ct));

            var payment = await conn.QuerySingleOrDefaultAsync<FeePaymentResponse>(new CommandDefinition(
                """
                SELECT Id, TenantId, StudentId, StudentName, ClassLabel, FeeType, Amount, Method, Ref, [Date], InvoiceId, HeadId
                FROM dbo.FeePayments WHERE Id = @payId
                """,
                new { payId }, tx, cancellationToken: ct));

            await auditLogger.LogAsync(conn, tx, new AuditEntry(
                tenantId, actorUserId, "FeePayment.Recorded", "Fees", "FeePayment", payId.ToString(),
                AfterData: new { Id = payId, InvoiceId = invoiceId, Amount = amount, Method = method }), ct);

            await tx.CommitAsync(ct);
            return payment;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<FeeInvoiceResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        var row = (await QueryInlineAsync<FeeInvoiceSqlRow>(
            $"{SelectJoined} WHERE i.Id = @id", new { id }, ct))
            .FirstOrDefault();
        return row?.ToResponse();
    }

    public async Task<IReadOnlyList<FeeInvoiceResponse>> ListAsync(Guid? studentId, CancellationToken ct = default)
    {
        await EnsurePaidAmountColumnAsync(ct);
        var rows = await QueryInlineAsync<FeeInvoiceSqlRow>(
            $"{SelectJoined} WHERE (@studentId IS NULL OR i.StudentId = @studentId) ORDER BY i.DueDate DESC",
            new { studentId }, ct);
        return rows.Select(r => r.ToResponse()).ToList();
    }

    public async Task<bool> ExistsForStudentPeriodAsync(Guid studentId, string period, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<int>(
            "SELECT TOP 1 1 FROM dbo.FeeInvoices WHERE StudentId = @studentId AND Period = @period",
            new { studentId, period }, ct);
        return rows.Count > 0;
    }

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

    private sealed class InvoiceLockRow
    {
        public Guid Id { get; set; }
        public Guid StudentId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "due";
        public decimal PaidAmount { get; set; }
    }

    private sealed record FeeInvoiceCore(
        Guid Id, Guid TenantId, Guid StudentId, string? Period, DateTime? DueDate, decimal Amount,
        string Status, DateTime? PaidOn, string? Method);

    private sealed class FeeInvoiceSqlRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid StudentId { get; set; }
        public string? Period { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "due";
        public DateTime? PaidOn { get; set; }
        public string? Method { get; set; }
        public string? StudentName { get; set; }
        public string? ClassLabel { get; set; }
        public string? AdmissionNo { get; set; }
        public string? Grade { get; set; }
        public int? AvatarHue { get; set; }
        public string? PhotoUrl { get; set; }
        public decimal PaidAmount { get; set; }

        public FeeInvoiceResponse ToResponse() => new(
            Id, TenantId, StudentId, Period, DueDate, Amount, Status, PaidOn, Method,
            StudentName, ClassLabel, AdmissionNo, Grade, AvatarHue, PhotoUrl, PaidAmount);
    }
}

public sealed record FeeTenantSummaryRow(
    Guid TenantId, string Name, decimal Collected, decimal Outstanding, int PaymentCount, int InvoiceCount);

/// <summary>CRM Fees collection KPIs — GET /v1/fees/reports/summary.</summary>
public sealed record FeeReportByClass(string Label, decimal Value, int N);
public sealed record FeeReportByMode(string Label, decimal Value);
public sealed record FeeReportLatestPayment(
    Guid Id, Guid StudentId, string? StudentName, string? Cls, decimal Amount,
    string? Mode, string? Ref, DateTime Date, string? HeadId = null);
public sealed record FeeReportSummaryResponse(
    decimal CollectedToday, decimal CollectedTerm, decimal Outstanding, int Defaulters,
    decimal BilledTerm, decimal Pct,
    IReadOnlyList<FeeReportByClass> ByClass,
    IReadOnlyList<FeeReportByMode> ByMode,
    FeeReportLatestPayment? LatestPayment);

public sealed record GenerateFeeInvoicesRequest(
    string AcademicYear,
    string Term,
    DateTime? DueDate,
    IReadOnlyList<string>? Grades,
    IReadOnlyList<string>? Classes);

public sealed record GenerateFeeInvoicesResponse(int Created);

// ---- Payslips (HR/payroll) ----
public sealed record PayslipResponse(
    Guid Id, Guid TenantId, Guid UserId, string? Month, int Year, decimal Gross, decimal Deductions, decimal Net, string Status,
    decimal Basic, decimal Hra, decimal Allowances, decimal Epf, decimal ProfTax, decimal OtherDeductions);
public sealed record CreatePayslipRequest(Guid UserId, string? Month, int Year, decimal Gross, decimal Deductions, decimal Net);

public sealed class PayslipRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, TenantId, UserId, Month, Year, Gross, Deductions, Net, Status, Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions";

    public Task<PayslipResponse?> CreateAsync(Guid tenantId, CreatePayslipRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<PayslipResponse>("dbo.Payslip_Create",
            new { TenantId = tenantId, r.UserId, r.Month, r.Year, r.Gross, r.Deductions, r.Net }, ct);

    public Task<IReadOnlyList<PayslipResponse>> ListAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        QueryInlineAsync<PayslipResponse>(
            $"SELECT {Cols} FROM dbo.Payslips WHERE TenantId = @tenantId AND UserId = @userId ORDER BY Year DESC, Month DESC",
            new { tenantId, userId }, ct);

    /// Replace any payslip for this user/period and publish payroll figures (mobile payslip feed).
    public async Task PublishForUserAsync(
        Guid tenantId, Guid userId, string? month, int year,
        decimal basic, decimal hra, decimal allowances, decimal epf, decimal profTax, decimal otherDeductions,
        decimal gross, decimal deductions, decimal net, string status = "paid", CancellationToken ct = default)
    {
        var slipStatus = string.IsNullOrWhiteSpace(status) ? "pending" : status.Trim().ToLowerInvariant();
        await ExecuteInlineAsync(
            "DELETE FROM dbo.Payslips WHERE TenantId=@tenantId AND UserId=@userId AND Month=@month AND Year=@year",
            new { tenantId, userId, month, year }, ct);
        await ExecuteInlineAsync(
            """
            INSERT dbo.Payslips (TenantId, UserId, Month, Year, Gross, Deductions, Net, Status,
                Basic, Hra, Allowances, Epf, ProfTax, OtherDeductions)
            VALUES (@tenantId, @userId, @month, @year, @gross, @deductions, @net, @status,
                @basic, @hra, @allowances, @epf, @profTax, @otherDeductions)
            """,
            new
            {
                tenantId, userId, month, year, gross, deductions, net, status = slipStatus,
                basic, hra, allowances, epf, profTax, otherDeductions,
            }, ct);
    }
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

    public Task<IReadOnlyList<PayrollRunResponse>> ListApprovedRunsAsync(Guid tenantId, CancellationToken ct = default) =>
        QueryInlineAsync<PayrollRunResponse>(
            """
            SELECT Id, TenantId, Period, Year, Month, Status, StaffCount, Gross, Deductions, Net,
                   RunBy, RunAt, ApprovedBy, ApprovedAt
            FROM dbo.PayrollRuns
            WHERE TenantId = @tenantId AND Status IN ('run', 'approved')
            ORDER BY Period DESC
            """,
            new { tenantId }, ct);

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

// ---- Fee heads (catalog of fee types) ----
public sealed record FeeHeadResponse(
    Guid Id, Guid TenantId, string Name, string? Code, bool Active, bool IsSystem);

public sealed record CreateFeeHeadRequest(string Name, string? Code);
public sealed record UpdateFeeHeadRequest(string? Name, string? Code, bool? Active);

public sealed class FeeHeadRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<IReadOnlyList<FeeHeadResponse>> ListAsync(CancellationToken ct = default) =>
        QueryProcAsync<FeeHeadResponse>("dbo.FeeHead_List", ct: ct);

    public Task<FeeHeadResponse?> CreateAsync(Guid tenantId, CreateFeeHeadRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeeHeadResponse>("dbo.FeeHead_Create", new
        {
            TenantId = tenantId,
            Name = r.Name.Trim(),
            Code = string.IsNullOrWhiteSpace(r.Code) ? null : r.Code.Trim(),
            Active = true,
            IsSystem = false,
        }, ct);

    public Task<FeeHeadResponse?> UpdateAsync(
        Guid id, Guid tenantId, UpdateFeeHeadRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeeHeadResponse>("dbo.FeeHead_Update", new
        {
            Id = id,
            TenantId = tenantId,
            Name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name.Trim(),
            Code = r.Code is null ? null : (string.IsNullOrWhiteSpace(r.Code) ? null : r.Code.Trim()),
            CodeSpecified = r.Code is not null,
            Active = r.Active,
        }, ct);

    public async Task<bool> DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var row = await QuerySingleProcAsync<DeleteCountRow>("dbo.FeeHead_Delete",
            new { Id = id, TenantId = tenantId }, ct);
        return row is { Deleted: > 0 };
    }

    private sealed record DeleteCountRow(int Deleted);
}

// ---- Fee structure (named document + class×head amounts JSON) ----
public sealed record FeeStructureRow(
    Guid Id, Guid TenantId, string Name, string AcademicYear, string? ClassGrade, string? Section,
    string Currency, DateTime EffectiveFrom, DateTime? EffectiveTo, string Status,
    string? Description, string AmountsJson);

public sealed record FeeStructureResponse(
    Guid? Id, Guid? TenantId, string Name, string AcademicYear,
    [property: JsonPropertyName("class")] string? ClassGrade,
    string? Section, string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string Status, string? Description, JsonElement Amounts);

public sealed record UpsertFeeStructureRequest(
    Guid? Id,
    string Name,
    string AcademicYear,
    [property: JsonPropertyName("class")] string? ClassGrade,
    string? Section,
    string Currency,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Status,
    string? Description,
    JsonElement? Amounts,
    string? AmountsJson = null);

public sealed class FeeStructureRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<FeeStructureRow?> GetAsync(CancellationToken ct = default) =>
        QuerySingleProcAsync<FeeStructureRow>("dbo.FeeStructure_Get", ct: ct);

    public Task<FeeStructureRow?> UpsertAsync(
        Guid tenantId, UpsertFeeStructureRequest r, string amountsJson, CancellationToken ct = default) =>
        QuerySingleProcAsync<FeeStructureRow>("dbo.FeeStructure_Upsert", new
        {
            TenantId = tenantId,
            r.Id,
            Name = r.Name.Trim(),
            AcademicYear = r.AcademicYear.Trim(),
            ClassGrade = string.IsNullOrWhiteSpace(r.ClassGrade) ? null : r.ClassGrade.Trim(),
            Section = string.IsNullOrWhiteSpace(r.Section) ? null : r.Section.Trim(),
            Currency = r.Currency.Trim(),
            EffectiveFrom = r.EffectiveFrom!.Value.ToDateTime(TimeOnly.MinValue),
            EffectiveTo = r.EffectiveTo?.ToDateTime(TimeOnly.MinValue),
            Status = string.IsNullOrWhiteSpace(r.Status) ? "active" : r.Status.Trim().ToLowerInvariant(),
            Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
            AmountsJson = amountsJson,
        }, ct);
}

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<FeeRepository>();
        services.AddScoped<FeeInvoiceRepository>();
        services.AddScoped<FeeHeadRepository>();
        services.AddScoped<FeeStructureRepository>();
        services.AddScoped<PayslipRepository>();
        services.AddScoped<PayrollRepository>();
        return services;
    }
}
