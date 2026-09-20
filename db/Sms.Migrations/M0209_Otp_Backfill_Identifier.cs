using FluentMigrator;

namespace Sms.Migrations;

/// M0033 added Identifier/Channel columns to OtpCodes but never copied existing Phone values
/// into Identifier, so any unexpired OTP issued before M0033 deployed became unverifiable —
/// Otp_GetActive/Otp_Consume (added by M0033) query only Identifier, never Phone. Backfills the
/// gap; a second run is a no-op since the WHERE clause only matches rows still missing Identifier.
[Migration(209, "OtpCodes: backfill Identifier/Channel from legacy Phone column")]
public sealed class M0209_Otp_Backfill_Identifier : Migration
{
    public override void Up() =>
        Execute.Sql(@"
            UPDATE dbo.OtpCodes
            SET Identifier = Phone, Channel = 'sms'
            WHERE Identifier IS NULL AND Phone IS NOT NULL;");

    public override void Down()
    {
        // Data backfill only — not reversible (and not meaningful to reverse).
    }
}
