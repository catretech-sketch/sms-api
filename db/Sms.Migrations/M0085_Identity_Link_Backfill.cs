using FluentMigrator;

namespace Sms.Migrations;

/// <summary>
/// Best-effort backfill of the identity-link columns added in M0084: match each
/// Teachers/Staff row to a Users row in the SAME tenant by email/phone, and record
/// every row that didn't get a clean single match in a report table for manual review.
///
/// RLS note: dbo.Users/Teachers/Staff all carry a tenant FILTER PREDICATE (see
/// M0002_Rls_Policies, M0013_Staffing_Tables) that hides rows unless SESSION_CONTEXT('TenantId')
/// matches or SESSION_CONTEXT('IsPlatform') = 1. The migration runner's connection sets neither,
/// so without elevation this backfill would silently see zero rows and no-op. Elevate to
/// IsPlatform=1 for this connection/session (same pattern as M0077_Trips_BusId's cross-tenant
/// BusId backfill) so the migration can read/write across all tenants; every join/WHERE clause
/// still scopes each match to the row's own TenantId, so no data crosses tenants.
/// </summary>
[Migration(85, "Identity-link backfill: best-effort Teachers/Staff <-> Users match by email/phone, unmatched report")]
public sealed class M0085_Identity_Link_Backfill : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE TABLE dbo._Migration_UnmatchedDirectoryRows (
    Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
    SourceTable nvarchar(20) NOT NULL,
    SourceId uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    Reason nvarchar(20) NOT NULL,
    MatchCount int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);");

        // Elevate this connection/session so the backfill can see rows across ALL tenants.
        // Every statement below still joins/filters on TenantId = TenantId (same tenant only);
        // this only lifts the RLS filter predicate that would otherwise hide every row from
        // the migration runner's own (non-elevated) session.
        Execute.Sql("EXEC sp_set_session_context @key=N'IsPlatform', @value=1;");

        // Teachers -> Users: link only when exactly one Users row in the same tenant
        // matches by email or phone (case-insensitive, trimmed email).
        Execute.Sql(@"
UPDATE t
SET t.UserId = m.MatchedUserId
FROM dbo.Teachers t
CROSS APPLY (
    SELECT TOP 1 u.Id AS MatchedUserId
    FROM dbo.Users u
    WHERE u.TenantId = t.TenantId
      AND ((t.Email IS NOT NULL AND u.Email IS NOT NULL
              AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(LTRIM(RTRIM(t.Email))))
        OR (t.Phone IS NOT NULL AND u.Phone IS NOT NULL AND u.Phone = t.Phone))
) m
WHERE t.UserId IS NULL
  AND (
    SELECT COUNT(*) FROM dbo.Users u2
    WHERE u2.TenantId = t.TenantId
      AND ((t.Email IS NOT NULL AND u2.Email IS NOT NULL
              AND LOWER(LTRIM(RTRIM(u2.Email))) = LOWER(LTRIM(RTRIM(t.Email))))
        OR (t.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = t.Phone))
  ) = 1;");

        Execute.Sql(@"
UPDATE u
SET u.Name = t.Name
FROM dbo.Users u
JOIN dbo.Teachers t ON t.UserId = u.Id
WHERE u.Name IS NULL;");

        // Staff -> Users: same pattern.
        Execute.Sql(@"
UPDATE s
SET s.UserId = m.MatchedUserId
FROM dbo.Staff s
CROSS APPLY (
    SELECT TOP 1 u.Id AS MatchedUserId
    FROM dbo.Users u
    WHERE u.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u.Phone IS NOT NULL AND u.Phone = s.Phone)
) m
WHERE s.UserId IS NULL
  AND (
    SELECT COUNT(*) FROM dbo.Users u2
    WHERE u2.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = s.Phone)
  ) = 1;");

        Execute.Sql(@"
UPDATE u
SET u.Name = s.Name
FROM dbo.Users u
JOIN dbo.Staff s ON s.UserId = u.Id
WHERE u.Name IS NULL;");

        // Report every Teachers/Staff row that didn't get a clean single match.
        Execute.Sql(@"
INSERT INTO dbo._Migration_UnmatchedDirectoryRows (SourceTable, SourceId, TenantId, Reason, MatchCount)
SELECT 'Teachers', t.Id, t.TenantId, CASE WHEN x.Cnt = 0 THEN 'no_match' ELSE 'ambiguous' END, x.Cnt
FROM dbo.Teachers t
CROSS APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Users u2
    WHERE u2.TenantId = t.TenantId
      AND ((t.Email IS NOT NULL AND u2.Email IS NOT NULL
              AND LOWER(LTRIM(RTRIM(u2.Email))) = LOWER(LTRIM(RTRIM(t.Email))))
        OR (t.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = t.Phone))
) x
WHERE t.UserId IS NULL AND x.Cnt <> 1;");

        Execute.Sql(@"
INSERT INTO dbo._Migration_UnmatchedDirectoryRows (SourceTable, SourceId, TenantId, Reason, MatchCount)
SELECT 'Staff', s.Id, s.TenantId, CASE WHEN x.Cnt = 0 THEN 'no_match' ELSE 'ambiguous' END, x.Cnt
FROM dbo.Staff s
CROSS APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Users u2
    WHERE u2.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = s.Phone)
) x
WHERE s.UserId IS NULL AND x.Cnt <> 1;");
    }

    public override void Down()
    {
        // No-op: backfilled data and the report table are historical record, not restorable/reversible.
    }
}
