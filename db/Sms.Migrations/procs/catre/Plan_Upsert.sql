CREATE OR ALTER PROCEDURE dbo.Plan_Upsert
    @Id uniqueidentifier,
    @Name nvarchar(100), @Tier nvarchar(20), @Pricing nvarchar(20),
    @Price decimal(18,2), @PerStudent decimal(18,2), @MinStudents int, @Period nvarchar(20),
    @FeaturesCsv nvarchar(4000), @LimitsStudents int, @LimitsStaff int, @LimitsStorageGb int,
    @Visibility nvarchar(20), @Audience nvarchar(20), @Band nvarchar(100),
    @OfferLabel nvarchar(100), @OfferPct int, @Color nvarchar(40), @Description nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Id IS NULL SET @Id = NEWID();

    IF EXISTS (SELECT 1 FROM dbo.Plans WHERE Id = @Id)
        UPDATE dbo.Plans SET
            Name = @Name, Tier = @Tier, Pricing = @Pricing, Price = @Price, PerStudent = @PerStudent,
            MinStudents = @MinStudents, Period = @Period, FeaturesCsv = @FeaturesCsv,
            LimitsStudents = @LimitsStudents, LimitsStaff = @LimitsStaff, LimitsStorageGb = @LimitsStorageGb,
            Visibility = @Visibility, Audience = @Audience, Band = @Band,
            OfferLabel = @OfferLabel, OfferPct = @OfferPct, Color = @Color, Description = @Description
        WHERE Id = @Id;
    ELSE
        INSERT dbo.Plans (Id, Name, Tier, Pricing, Price, PerStudent, MinStudents, Period, FeaturesCsv,
            LimitsStudents, LimitsStaff, LimitsStorageGb, Visibility, Audience, Band,
            OfferLabel, OfferPct, Color, Description)
        VALUES (@Id, @Name, @Tier, @Pricing, @Price, @PerStudent, @MinStudents, @Period, @FeaturesCsv,
            @LimitsStudents, @LimitsStaff, @LimitsStorageGb, @Visibility, @Audience, @Band,
            @OfferLabel, @OfferPct, @Color, @Description);

    SELECT Id, Name, Tier, Pricing, Price, PerStudent, MinStudents, Period, FeaturesCsv,
           LimitsStudents, LimitsStaff, LimitsStorageGb, Visibility, Audience, Band,
           OfferLabel, OfferPct, Color, Description
    FROM dbo.Plans WHERE Id = @Id;
END
