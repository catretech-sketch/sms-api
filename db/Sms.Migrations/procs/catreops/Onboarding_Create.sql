CREATE OR ALTER PROCEDURE dbo.Onboarding_Create
    @Name nvarchar(200), @Slug nvarchar(100), @Owner nvarchar(120),
    @Value decimal(18,2), @Stage nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id uniqueidentifier = NEWID();
    INSERT dbo.OnboardingItems (Id, Name, Slug, Owner, Value, Stage)
    VALUES (@Id, @Name, @Slug, @Owner, ISNULL(@Value, 0), ISNULL(@Stage, 'lead'));

    INSERT dbo.OnboardingChecklist (OnboardingId, Seq, Label, Done) VALUES
        (@Id, 1, 'Account created', 0), (@Id, 2, 'Admin invited', 0), (@Id, 3, 'Data imported', 0),
        (@Id, 4, 'First login', 0), (@Id, 5, 'Payment set up', 0);

    SELECT @Id AS Id;
END
