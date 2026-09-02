using FluentMigrator;

namespace Sms.Migrations;

[Migration(164, "Staff self-service profile: StaffDocuments table + StaffDocuments_ListForUser proc")]
public sealed class M0164_StaffDocuments_Table : Migration
{
    public override void Up()
    {
        Create.Table("StaffDocuments")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StaffId").AsGuid().NotNullable()
            .WithColumn("Label").AsString(120).NotNullable()
            .WithColumn("Value").AsString(200).NotNullable()
            .WithColumn("Ok").AsBoolean().Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_StaffDocuments_Staff_CreatedAt").OnTable("StaffDocuments")
            .OnColumn("StaffId").Ascending().OnColumn("CreatedAt").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.StaffDocumentsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.StaffDocuments,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.StaffDocuments AFTER INSERT
WITH (STATE = ON);");

        // Own namespace fragment ("procs.staffingprofile.", not "procs.staffing.") so no
        // earlier migration's broad-prefix EmbeddedProcs("procs.staffing.") call (M0014) picks
        // this up and tries to create it before the StaffDocuments table above exists in a
        // fresh-DB replay — the exact class of bug fixed in M0024/M0163 for Transport.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingprofile.StaffDocuments_ListForUser"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.StaffDocuments_ListForUser;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.StaffDocumentsTenantPolicy;");
        Delete.Table("StaffDocuments");
    }
}
