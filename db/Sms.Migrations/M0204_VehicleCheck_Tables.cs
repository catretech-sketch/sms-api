using FluentMigrator;

namespace Sms.Migrations;

[Migration(204, "VehicleChecks: VehicleInspections + FuelLogs tables with tenant RLS")]
public sealed class M0204_VehicleCheck_Tables : Migration
{
    public override void Up()
    {
        Create.Table("VehicleInspections")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("SubmittedByUserId").AsGuid().NotNullable()
            .WithColumn("Brakes").AsBoolean().NotNullable()
            .WithColumn("Tyres").AsBoolean().NotNullable()
            .WithColumn("Lights").AsBoolean().NotNullable()
            .WithColumn("Horn").AsBoolean().NotNullable()
            .WithColumn("FirstAidKit").AsBoolean().NotNullable()
            .WithColumn("FireExtinguisher").AsBoolean().NotNullable()
            .WithColumn("EmergencyExit").AsBoolean().NotNullable()
            .WithColumn("FuelLevel").AsBoolean().NotNullable()
            .WithColumn("AllOk").AsBoolean().NotNullable()
            .WithColumn("Remarks").AsString(2000).Nullable()
            .WithColumn("InspectionDate").AsDate().NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_VehicleInspections_Tenant").OnTable("VehicleInspections").OnColumn("TenantId").Ascending();
        Create.Index("IX_VehicleInspections_Bus").OnTable("VehicleInspections").OnColumn("BusId").Ascending();
        // "One inspection per bus per day": resubmitting the same bus on the same day is an
        // upsert (dbo.VehicleInspection_Upsert MERGEs on this exact key), never a second row.
        Create.Index("UX_VehicleInspections_Tenant_Bus_Date").OnTable("VehicleInspections")
            .OnColumn("TenantId").Ascending().OnColumn("BusId").Ascending().OnColumn("InspectionDate").Ascending()
            .WithOptions().Unique();

        Create.Table("FuelLogs")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("BusId").AsGuid().NotNullable()
            .WithColumn("RecordedByUserId").AsGuid().NotNullable()
            .WithColumn("OdometerKm").AsInt32().NotNullable()
            .WithColumn("FuelAddedLiters").AsDecimal(10, 2).NotNullable()
            .WithColumn("RecordedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("IX_FuelLogs_Tenant").OnTable("FuelLogs").OnColumn("TenantId").Ascending();
        // Append-only history: every fuel-up is its own row, deliberately no uniqueness
        // constraint — unlike VehicleInspections, several same-day entries for the same bus
        // must all persist.
        Create.Index("IX_FuelLogs_Bus_RecordedAt").OnTable("FuelLogs")
            .OnColumn("BusId").Ascending().OnColumn("RecordedAt").Descending();

        foreach (var t in new[] { "VehicleInspections", "FuelLogs" })
            Execute.Sql($@"
CREATE SECURITY POLICY rls.{t}TenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t},
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.{t} AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        foreach (var t in new[] { "FuelLogs", "VehicleInspections" })
            Execute.Sql($"DROP SECURITY POLICY IF EXISTS rls.{t}TenantPolicy;");
        Delete.Table("FuelLogs");
        Delete.Table("VehicleInspections");
    }
}
