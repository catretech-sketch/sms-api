using FluentMigrator;

namespace Sms.Migrations;

[Migration(134, "Students.GuardianEmail + Parent_EnsureLogin so parent app login matches enrolment mail")]
public sealed class M0134_GuardianEmailParentLogin : Migration
{
    public override void Up()
    {
        if (!Schema.Table("Students").Column("GuardianEmail").Exists())
            Alter.Table("Students").AddColumn("GuardianEmail").AsString(256).Nullable();

        Execute.Sql("""
UPDATE s
SET GuardianEmail = LEFT(LTRIM(RTRIM(COALESCE(
    NULLIF(JSON_VALUE(pe.ExtrasJson, '$.father.email'), N''),
    NULLIF(JSON_VALUE(pe.ExtrasJson, '$.mother.email'), N'')
))), 256)
FROM dbo.Students s
INNER JOIN dbo.PersonExtras pe
    ON pe.PersonId = s.Id AND pe.PersonType = N'student' AND pe.TenantId = s.TenantId
WHERE s.GuardianEmail IS NULL
  AND (
        NULLIF(LTRIM(RTRIM(JSON_VALUE(pe.ExtrasJson, '$.father.email'))), N'') IS NOT NULL
     OR NULLIF(LTRIM(RTRIM(JSON_VALUE(pe.ExtrasJson, '$.mother.email'))), N'') IS NOT NULL
  );
""");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisguardian.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisguardian.Student_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Parent_EnsureLogin"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identityparent.Student_GetByAdmissionNo"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisemail.Student_Update"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.sisphoto.Student_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.identitylogin.Student_GetByAdmissionNo"))
            Execute.Sql(sql);
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Parent_EnsureLogin;");
        if (Schema.Table("Students").Column("GuardianEmail").Exists())
            Delete.Column("GuardianEmail").FromTable("Students");
    }
}
