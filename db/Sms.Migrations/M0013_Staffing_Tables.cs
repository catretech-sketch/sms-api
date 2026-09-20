using FluentMigrator;

namespace Sms.Migrations;

[Migration(13, "Staffing: Teachers + Staff tables + tenant RLS policies")]
public sealed class M0013_Staffing_Tables : Migration
{
    public override void Up()
    {
        Create.Table("Teachers")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Gender").AsString(1).Nullable()
            .WithColumn("Department").AsString(80).Nullable()
            .WithColumn("Designation").AsString(80).Nullable()
            .WithColumn("SubjectsCsv").AsString(400).Nullable()
            .WithColumn("ClassTeacher").AsString(40).Nullable()
            .WithColumn("Phone").AsString(40).Nullable()
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("Exp").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Rating").AsDecimal(4, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("AttendancePct").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Result").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Load").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("AvatarHue").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Top").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Teachers_Tenant").OnTable("Teachers").OnColumn("TenantId").Ascending();

        Create.Table("Staff")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Gender").AsString(1).Nullable()
            .WithColumn("Role").AsString(80).Nullable()
            .WithColumn("Category").AsString(40).Nullable()
            .WithColumn("Department").AsString(80).Nullable()
            .WithColumn("Phone").AsString(40).Nullable()
            .WithColumn("Shift").AsString(40).Nullable()
            .WithColumn("Route").AsString(80).Nullable()
            .WithColumn("AttendancePct").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("active")
            .WithColumn("AvatarHue").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
        Create.Index("IX_Staff_Tenant").OnTable("Staff").OnColumn("TenantId").Ascending();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.TeachersTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Teachers,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Teachers AFTER INSERT
WITH (STATE = ON);");
        Execute.Sql(@"
CREATE SECURITY POLICY rls.StaffTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Staff,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.Staff AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.StaffTenantPolicy;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.TeachersTenantPolicy;");
        Delete.Table("Staff");
        Delete.Table("Teachers");
    }
}
