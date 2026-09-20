using FluentMigrator;

namespace Sms.Migrations;

[Migration(14, "Staffing procs: Teacher_Create/Update + Staff_Create/Update (embedded CREATE OR ALTER)")]
public sealed class M0014_Procs_Staffing : Migration
{
    public override void Up()
    {
        // The staffing procs reference Teachers/Staff.EmployeeCode from their first creation here.
        // SQL Server validates columns of EXISTING tables at CREATE PROCEDURE time (deferred name
        // resolution only covers missing tables, not missing columns), so the column must exist
        // before the procs are created. It is formally added + backfilled later in M0063; this
        // guarded add keeps every earlier proc (re)creation valid and is a no-op once M0063 runs.
        Execute.Sql(@"
IF COL_LENGTH('dbo.Teachers', 'EmployeeCode') IS NULL
    ALTER TABLE dbo.Teachers ADD EmployeeCode nvarchar(64) NULL;
IF COL_LENGTH('dbo.Staff', 'EmployeeCode') IS NULL
    ALTER TABLE dbo.Staff ADD EmployeeCode nvarchar(64) NULL;
");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffing."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Teacher_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Teacher_Update;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_Create;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Staff_Update;");
    }
}
