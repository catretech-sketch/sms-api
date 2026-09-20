using FluentMigrator;

namespace Sms.Migrations;

/// Security review fixes (Copilot PR review): three procs previously had no tenant/atomicity
/// guard, letting a caller in one tenant read/write another tenant's row by id, or letting two
/// concurrent OTP verify/reset requests both win.
/// - Complaint_Update / Complaint (Get) now require @TenantId.
/// - Trip_End now requires @TenantId.
/// - Otp_Consume now returns the affected row count so a losing concurrent request can be
///   rejected instead of silently succeeding.
/// Redeploys the updated proc bodies (CREATE OR ALTER is idempotent) on a database that already
/// ran M0032/M0024/M0033, matching the M0177-style proc-reload pattern used elsewhere.
[Migration(208, "Security review: tenant-scope Complaint_Update/Trip_End, make Otp_Consume atomic")]
public sealed class M0208_Security_Review_Proc_Fixes : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.tails.Complaint_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.transport.Trip_End"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.Otp_Consume"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // Proc bodies are not reversible to "the previous version" (CREATE OR ALTER isn't
        // naturally undoable) — matches the established pattern for proc-fix migrations.
    }
}
