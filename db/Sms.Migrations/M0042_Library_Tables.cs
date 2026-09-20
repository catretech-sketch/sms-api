using FluentMigrator;

namespace Sms.Migrations;

[Migration(42, "Library: LibraryBooks table + tenant RLS + insert proc")]
public sealed class M0042_Library_Tables : Migration
{
    public override void Up()
    {
        Create.Table("LibraryBooks")
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefault(SystemMethods.NewSequentialId)
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("Title").AsString(200).NotNullable()
            .WithColumn("Author").AsString(120).NotNullable()
            .WithColumn("Subject").AsString(80).Nullable()
            .WithColumn("IssuedTo").AsString(120).Nullable()
            .WithColumn("DueDate").AsDate().Nullable()
            .WithColumn("Status").AsString(20).NotNullable().WithDefaultValue("available");
        Create.Index("IX_LibraryBooks_Tenant").OnTable("LibraryBooks").OnColumn("TenantId").Ascending();

        Execute.Sql(@"CREATE SECURITY POLICY rls.LibraryBooksTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.LibraryBooks,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.LibraryBooks AFTER INSERT
WITH (STATE = ON);");

        Execute.Sql(@"CREATE OR ALTER PROCEDURE dbo.LibraryBook_Create
    @TenantId uniqueidentifier, @Title nvarchar(200), @Author nvarchar(120),
    @Subject nvarchar(80) = NULL, @IssuedTo nvarchar(120) = NULL, @DueDate date = NULL,
    @Status nvarchar(20) = 'available'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ins TABLE (Id uniqueidentifier);
    INSERT dbo.LibraryBooks (TenantId, Title, Author, Subject, IssuedTo, DueDate, Status)
    OUTPUT inserted.Id INTO @ins
    VALUES (@TenantId, @Title, @Author, @Subject, @IssuedTo, @DueDate, @Status);
    SELECT Id, TenantId, Title, Author, Subject, IssuedTo, DueDate, Status
    FROM dbo.LibraryBooks WHERE Id = (SELECT Id FROM @ins);
END;");
    }

    public override void Down()
    {
        Execute.Sql("DROP PROCEDURE IF EXISTS dbo.LibraryBook_Create;");
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.LibraryBooksTenantPolicy;");
        Delete.Table("LibraryBooks");
    }
}
