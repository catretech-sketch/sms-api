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
///
/// SESSION_CONTEXT persistence: rather than relying on SESSION_CONTEXT set in one Execute.Sql
/// call surviving into later, separate Execute.Sql calls (unverified for FluentMigrator's
/// connection lifecycle), every RLS-touching statement below bundles its own
/// "EXEC sp_set_session_context ...;" as the first statement of the SAME Execute.Sql batch,
/// exactly like M0077_Trips_BusId.cs's proven single-Execute.Sql-string structure. This makes
/// each statement correct independent of whether the connection/session is later reused, so
/// there is nothing to prove empirically.
///
/// Idempotency: the report table creation and both report INSERTs are re-run-safe (IF OBJECT_ID
/// guard on CREATE TABLE; NOT EXISTS guard on each INSERT) so manually re-executing this file's
/// raw SQL outside FluentMigrator's normal VersionInfo gate does not duplicate report rows.
/// </summary>
[Migration(85, "Identity-link backfill: best-effort Teachers/Staff <-> Users match by email/phone, unmatched report")]
public sealed class M0085_Identity_Link_Backfill : Migration
{
    private const string ElevateToPlatform = "EXEC sp_set_session_context @key=N'IsPlatform', @value=1;\n";

    public override void Up()
    {
        Execute.Sql(@"
IF OBJECT_ID('dbo._Migration_UnmatchedDirectoryRows') IS NULL
CREATE TABLE dbo._Migration_UnmatchedDirectoryRows (
    Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
    SourceTable nvarchar(20) NOT NULL,
    SourceId uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    Reason nvarchar(20) NOT NULL,
    MatchCount int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);");

        // Teachers -> Users: link only when exactly one Users row in the same tenant
        // matches by email or phone (case-insensitive, trimmed email). The elevation is
        // bundled into this SAME Execute.Sql batch (not a prior, separate call) so the
        // RLS lift is guaranteed to be in effect for this statement.
        Execute.Sql(ElevateToPlatform + @"
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

        Execute.Sql(ElevateToPlatform + @"
UPDATE u
SET u.Name = t.Name
FROM dbo.Users u
JOIN dbo.Teachers t ON t.UserId = u.Id
WHERE u.Name IS NULL;");

        // Staff -> Users: same pattern.
        Execute.Sql(ElevateToPlatform + @"
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

        Execute.Sql(ElevateToPlatform + @"
UPDATE u
SET u.Name = s.Name
FROM dbo.Users u
JOIN dbo.Staff s ON s.UserId = u.Id
WHERE u.Name IS NULL;");

        // Report every Teachers/Staff row that didn't get a clean single match. Guarded with
        // NOT EXISTS so re-running this INSERT (e.g. the raw SQL run by hand outside FluentMigrator's
        // normal VersionInfo gate) does not duplicate report rows for a SourceId already reported.
        Execute.Sql(ElevateToPlatform + @"
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
WHERE t.UserId IS NULL AND x.Cnt <> 1
  AND NOT EXISTS (
    SELECT 1 FROM dbo._Migration_UnmatchedDirectoryRows r
    WHERE r.SourceTable = 'Teachers' AND r.SourceId = t.Id);");

        Execute.Sql(ElevateToPlatform + @"
INSERT INTO dbo._Migration_UnmatchedDirectoryRows (SourceTable, SourceId, TenantId, Reason, MatchCount)
SELECT 'Staff', s.Id, s.TenantId, CASE WHEN x.Cnt = 0 THEN 'no_match' ELSE 'ambiguous' END, x.Cnt
FROM dbo.Staff s
CROSS APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Users u2
    WHERE u2.TenantId = s.TenantId
      AND (s.Phone IS NOT NULL AND u2.Phone IS NOT NULL AND u2.Phone = s.Phone)
) x
WHERE s.UserId IS NULL AND x.Cnt <> 1
  AND NOT EXISTS (
    SELECT 1 FROM dbo._Migration_UnmatchedDirectoryRows r
    WHERE r.SourceTable = 'Staff' AND r.SourceId = s.Id);");
    }

    public override void Down()
    {
        // No-op: backfilled data and the report table are historical record, not restorable/reversible.
    }
}
