CREATE OR ALTER PROCEDURE dbo.Message_Add
    @TenantId uniqueidentifier, @ThreadId uniqueidentifier, @SenderId uniqueidentifier, @Text nvarchar(2000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    DECLARE @Now datetime2 = SYSUTCDATETIME();
    INSERT dbo.ChatMessages (Id, TenantId, ThreadId, SenderId, [Text], SentAt)
    VALUES (@Id, @TenantId, @ThreadId, @SenderId, @Text, @Now);

    UPDATE dbo.ChatThreads SET LastMessage = @Text, LastAt = @Now WHERE Id = @ThreadId;

    SELECT Id, ThreadId, SenderId, [Text], SentAt FROM dbo.ChatMessages WHERE Id = @Id;
END
