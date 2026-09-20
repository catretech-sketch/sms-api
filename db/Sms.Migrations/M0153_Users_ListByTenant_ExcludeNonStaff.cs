using FluentMigrator;

namespace Sms.Migrations;

/// Identity & access (GET /users) is meant to manage CRM/staff accounts, but the proc had no
/// role filter — students and parents (who have Users rows for their own app logins) showed
/// up in that list too, and the frontend's role mapper defaults any unrecognized role to
/// "Teacher" for display, so a parent/student account appeared mislabeled as a Teacher.
[Migration(153, "Users_ListByTenant: only CRM/staff-role accounts, not every tenant user")]
public sealed class M0153_Users_ListByTenant_ExcludeNonStaff : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.Users_ListByTenant"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        // No-op: the previous proc body (without the CRM-role filter) is superseded, not restored.
    }
}
