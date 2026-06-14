using System.Reflection;
using FluentMigrator;

namespace Sms.Migrations;

[Migration(3, "Auth stored procedures (idempotent CREATE OR ALTER from embedded .sql)")]
public sealed class M0003_Procs_Auth : Migration
{
    public override void Up()
    {
        foreach (var sql in EmbeddedProcs("procs.auth."))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_GetByEmail;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RefreshToken_Insert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RefreshToken_GetActive;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.RefreshToken_Revoke;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_GetByStudentId;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Otp_Insert;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Otp_GetActive;");
    }

    internal static IEnumerable<string> EmbeddedProcs(string namespaceFragment)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.Contains(namespaceFragment) && n.EndsWith(".sql"))
                     .OrderBy(n => n))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }
}
