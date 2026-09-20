using FluentMigrator;

namespace Sms.Migrations;

[Migration(146, "Leave_Decide: drop extra Note column from SELECT; return who decided")]
public sealed class M0146_Leave_Decide_DecidedByName : Migration
{
    private const string LeaveDecide = """
        CREATE OR ALTER PROCEDURE dbo.Leave_Decide
            @Id uniqueidentifier, @Status nvarchar(20), @DecidedBy uniqueidentifier, @DecidedNote nvarchar(500)
        AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE dbo.LeaveRequests SET Status = @Status, DecidedBy = @DecidedBy, DecidedNote = @DecidedNote WHERE Id = @Id;

            SELECT lr.Id, lr.TenantId, lr.RequesterId, lr.ChildId, lr.Type, lr.FromDate, lr.ToDate,
                   lr.Reason, lr.Substitute, lr.Status, lr.AppliedOn, lr.DecidedNote, lr.Priority, lr.AttachmentUrls,
                   u.Name AS RequesterName, d.Name AS DecidedByName
            FROM dbo.LeaveRequests lr
            LEFT JOIN dbo.Users u ON u.Id = lr.RequesterId
            LEFT JOIN dbo.Users d ON d.Id = lr.DecidedBy
            WHERE lr.Id = @Id;
        END
        """;

    public override void Up() => Execute.Sql(LeaveDecide);

    public override void Down() { }
}
