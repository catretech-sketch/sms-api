using FluentMigrator;

namespace Sms.Migrations;

[Migration(11, "SIS: Students table + tenant Row-Level Security policy")]
public sealed class M0011_Sis_Students : Migration
{
    public override void Up()
    {
        Create.Table("Students")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("AdmissionNo").AsString(64).NotNullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Gender").AsString(1).Nullable()
            .WithColumn("Grade").AsString(20).Nullable()
            .WithColumn("Section").AsString(20).Nullable()
            .WithColumn("ClassLabel").AsString(40).Nullable()
            .WithColumn("Roll").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("GuardianName").AsString(200).Nullable()
            .WithColumn("GuardianPhone").AsString(40).Nullable()
            .WithColumn("AttendancePct").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("FeeStatus").AsString(20).NotNullable().WithDefaultValue("due")
            .WithColumn("FeeDue").AsDecimal(18, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("House").AsString(40).Nullable()
            .WithColumn("AvatarHue").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Dob").AsDateTime2().Nullable()
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("Address").AsString(500).Nullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Students_Tenant_Class").OnTable("Students")
            .OnColumn("TenantId").Ascending().OnColumn("Grade").Ascending().OnColumn("Section").Ascending();

        // Tenant isolation: rows visible only to their tenant's session (or platform). Reuses the
        // predicate created in M0002. BLOCK predicate stops cross-tenant inserts.
        Execute.Sql(@"
CREATE SECURITY POLICY rls.StudentsTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Students,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Students AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.StudentsTenantPolicy;");
        Delete.Table("Students");
    }
}
