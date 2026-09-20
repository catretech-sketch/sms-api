using System.Linq;
using FluentMigrator;

namespace Sms.Migrations;

[Migration(79, "Invitations lifecycle: Invitations table with tenant RLS + resend/revoke/status procs")]
public sealed class M0079_Invitations_Table : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
IF OBJECT_ID('dbo.Invitations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Invitations (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Invitations PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        Email nvarchar(256) NULL,
        Phone nvarchar(32) NULL,
        RoleLabel nvarchar(64) NOT NULL,
        InvitedByUserId uniqueidentifier NULL,
        InvitedAt datetime2 NOT NULL CONSTRAINT DF_Invitations_InvitedAt DEFAULT (SYSUTCDATETIME()),
        ExpiresAt datetime2 NOT NULL,
        AcceptedAt datetime2 NULL,
        RevokedAt datetime2 NULL,
        LastResentAt datetime2 NULL,
        CONSTRAINT FK_Invitations_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
    );
    CREATE INDEX IX_Invitations_Tenant ON dbo.Invitations(TenantId);
    CREATE INDEX IX_Invitations_UserId ON dbo.Invitations(UserId);
END
");

        Execute.Sql(@"CREATE SECURITY POLICY rls.InvitationsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Invitations,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Invitations AFTER INSERT
WITH (STATE = ON);");

        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.saas.Invitations_")
            .Concat(M0003_Procs_Auth.EmbeddedProcs("procs.saas.User_SetStatus"))
            .Concat(M0003_Procs_Auth.EmbeddedProcs("procs.saas.Otp_ConsumeAllForIdentifier")))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.Otp_ConsumeAllForIdentifier;");
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.User_SetStatus;");
        foreach (var name in new[]
        {
            "Invitations_Create", "Invitations_ListByTenant", "Invitations_GetById",
            "Invitations_MarkResent", "Invitations_MarkAcceptedByUserId", "Invitations_MarkRevoked",
        })
            Execute.Sql($"DROP PROCEDURE IF EXISTS dbo.{name};");

        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.InvitationsTenantPolicy;");
        Delete.Table("Invitations");
    }
}
