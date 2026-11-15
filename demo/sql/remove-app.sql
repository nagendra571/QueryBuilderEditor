-- Remove a demo app from the hub completely, so scene 1 ("register App B") can be recorded again from scratch.
-- Run on the HUB's metadata database as its admin. Only you run this; it is never wired to a web page.
--
--   Look first:  sqlcmd -S tcp:<server>.database.windows.net,1433 -U <admin> -d hub -I -b -v AppKey="app-b" Confirm="no" -i remove-app.sql
--   Then remove: the same with Confirm="yes"
--
-- Both variables are required (sqlcmd refuses to run without them). With anything but Confirm="yes" it only lists what
-- it would remove.
--
-- Removes the app's row and everything that belongs to it: signing keys, return URLs, data sources and their scope rules,
-- queries, charts, dashboards, tiles, shares, people seen, launch replay records, and its audit entries (so the console's
-- audit view starts clean for the retake). The hub has no "delete app" screen; this is a demo-only shortcut.
--
-- Refused: the northwind app, the built-in Default app, an unknown key, and any app that someone has launched into
-- (it has SeenUsers), because that is a real app in use, not a never-launched App B.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @key nvarchar(64) = N'$(AppKey)';
DECLARE @confirm nvarchar(10) = N'$(Confirm)';
DECLARE @app uniqueidentifier = (SELECT Id FROM dbo.Apps WHERE [Key] = @key);

IF @key IN (N'northwind') THROW 50010, 'Refused: that is the demo''s App A.', 1;
IF @app IS NULL THROW 50011, 'No app with that key.', 1;
IF @app = '00000000-0000-0000-0000-00000000000d' THROW 50012, 'Refused: that is the built-in Default app.', 1;
IF EXISTS (SELECT 1 FROM dbo.SeenUsers WHERE AppId = @app) THROW 50013, 'Refused: people have launched into this app, so it is not a never-launched demo app.', 1;

SELECT a.[Key], a.Name, a.IsEnabled,
       (SELECT COUNT(*) FROM dbo.AppSigningKeys WHERE AppId = @app) AS SigningKeys,
       (SELECT COUNT(*) FROM dbo.AppReturnUrls WHERE AppId = @app) AS ReturnUrls,
       (SELECT COUNT(*) FROM dbo.DataSources WHERE AppId = @app) AS DataSources,
       (SELECT COUNT(*) FROM dbo.SavedQueries WHERE AppId = @app) AS Queries,
       (SELECT COUNT(*) FROM dbo.Dashboards WHERE AppId = @app) AS Dashboards,
       (SELECT COUNT(*) FROM dbo.AuditLogEntries WHERE AppId = @app) AS AuditEntries
FROM dbo.Apps a WHERE a.Id = @app;

IF @confirm <> N'yes'
BEGIN
    PRINT 'Nothing removed. Run again with -v Confirm="yes" to remove this app.';
    RETURN;
END

BEGIN TRANSACTION;
DELETE FROM dbo.DashboardShares     WHERE AppId = @app;
DELETE FROM dbo.DashboardTiles      WHERE AppId = @app;
DELETE FROM dbo.Dashboards          WHERE AppId = @app;
DELETE FROM dbo.QueryShares         WHERE AppId = @app;
DELETE FROM dbo.QueryVisualizations WHERE AppId = @app;
DELETE FROM dbo.SavedQueries        WHERE AppId = @app;
DELETE FROM dbo.DataScopeRules      WHERE AppId = @app;
DELETE FROM dbo.DataSources         WHERE AppId = @app;
DELETE FROM dbo.SeenUsers           WHERE AppId = @app;
DELETE FROM dbo.LaunchReplays       WHERE AppId = @app;
DELETE FROM dbo.AppSigningKeys      WHERE AppId = @app;
DELETE FROM dbo.AppReturnUrls       WHERE AppId = @app;
DELETE FROM dbo.AuditLogEntries     WHERE AppId = @app;
DELETE FROM dbo.Apps                WHERE Id = @app;
COMMIT;

PRINT 'Removed. The key can be registered again in the console.';
