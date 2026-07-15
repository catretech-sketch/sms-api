CREATE OR ALTER PROCEDURE dbo.Audit_Insert
    @ActorId uniqueidentifier = NULL,
    @ActorName nvarchar(200) = NULL,
    @Role nvarchar(80) = NULL,
    @Action nvarchar(200),
    @Target nvarchar(200) = NULL,
    @Kind nvarchar(40) = NULL,
    @TenantId uniqueidentifier = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.AuditLog (Id, ActorId, ActorName, Role, Action, Target, Kind, TenantId, At)
    VALUES (@Id, @ActorId, @ActorName, @Role, @Action, @Target, @Kind, @TenantId, SYSUTCDATETIME());
    SELECT Id, ActorId, ActorName, Role, Action, Target, Kind, At AS [Time]
    FROM dbo.AuditLog WHERE Id = @Id;
END
