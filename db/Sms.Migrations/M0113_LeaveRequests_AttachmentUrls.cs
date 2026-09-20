using FluentMigrator;

namespace Sms.Migrations;

[Migration(113, "LeaveRequests: AttachmentUrls + Leave_Create proc")]
public sealed class M0113_LeaveRequests_AttachmentUrls : Migration
{
    private const string LeaveCreateInline = """
        CREATE OR ALTER PROCEDURE dbo.Leave_Create
            @TenantId uniqueidentifier, @RequesterId uniqueidentifier, @ChildId uniqueidentifier, @Type nvarchar(20),
            @FromDate date, @ToDate date, @Reason nvarchar(500), @Substitute nvarchar(120), @Priority nvarchar(10) = 'medium',
            @AttachmentUrls nvarchar(max) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Id uniqueidentifier = NEWID();
            INSERT dbo.LeaveRequests (Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, AppliedOn, Priority, AttachmentUrls)
            VALUES (@Id, @TenantId, @RequesterId, @ChildId, ISNULL(@Type, 'casual'), @FromDate, @ToDate, @Reason,
                @Substitute, CAST(SYSUTCDATETIME() AS date), ISNULL(@Priority, 'medium'), @AttachmentUrls);

            SELECT Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote, Priority, AttachmentUrls
            FROM dbo.LeaveRequests WHERE Id = @Id;
        END
        """;

    private const string LeaveDecideInline = """
        CREATE OR ALTER PROCEDURE dbo.Leave_Decide
            @Id uniqueidentifier, @Status nvarchar(20), @DecidedBy uniqueidentifier, @DecidedNote nvarchar(500)
        AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE dbo.LeaveRequests SET Status = @Status, DecidedBy = @DecidedBy, DecidedNote = @DecidedNote WHERE Id = @Id;

            SELECT Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote, Priority, AttachmentUrls
            FROM dbo.LeaveRequests WHERE Id = @Id;
        END
        """;

    public override void Up()
    {
        if (!Schema.Table("LeaveRequests").Column("AttachmentUrls").Exists())
            Alter.Table("LeaveRequests")
                .AddColumn("AttachmentUrls").AsString(int.MaxValue).Nullable();

        Execute.Sql(LeaveCreateInline);
        Execute.Sql(LeaveDecideInline);
    }

    public override void Down()
    {
        if (Schema.Table("LeaveRequests").Column("AttachmentUrls").Exists())
            Delete.Column("AttachmentUrls").FromTable("LeaveRequests");
    }
}
