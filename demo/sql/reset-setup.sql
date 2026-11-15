-- Reset demo, hub side. Run on the HUB's metadata database as its admin, AFTER demo/setup/setup.mjs has created the
-- starting set. Safe to re-run. Lives in its own `demo` schema: the hub's migrations never touch it.
--
--   sqlcmd -S tcp:<server>.database.windows.net,1433 -U <admin> -d hub -I -b -v ResetPassword="<password>" -i reset-setup.sql
--
-- What it does:
--   1. demo.CaptureNorthwindBaseline copies the northwind app's current queries, charts, dashboards and tiles into
--      demo.* tables (the "starting set"). This script runs it once at the end. Run it again after you change the
--      starting set on purpose, or after a hub upgrade adds columns (the copy then picks up the new columns).
--   2. demo.ResetNorthwind deletes the app's queries, charts, dashboards, tiles and all shares, then puts the starting set
--      back with the same ids. It never touches the app's registration, keys, return URLs, data source, scope rules,
--      SeenUsers, the audit log, Data Protection keys or any other app.
--   3. A contained user demo_reset may EXECUTE demo.ResetNorthwind and nothing else. The Northwind app connects as it.

IF SCHEMA_ID('demo') IS NULL EXEC('CREATE SCHEMA demo AUTHORIZATION dbo');
GO

CREATE OR ALTER PROCEDURE demo.CaptureNorthwindBaseline
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @app uniqueidentifier = (SELECT Id FROM dbo.Apps WHERE [Key] = N'northwind');
    IF @app IS NULL THROW 50001, 'App "northwind" not found.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Dashboards WHERE AppId = @app) THROW 50002, 'Run demo/setup/setup.mjs first: the app has no dashboard.', 1;

    BEGIN TRANSACTION;
    DROP TABLE IF EXISTS demo.DashboardTiles, demo.Dashboards, demo.QueryVisualizations, demo.SavedQueries;
    SELECT * INTO demo.SavedQueries        FROM dbo.SavedQueries        WHERE AppId = @app;
    SELECT * INTO demo.QueryVisualizations FROM dbo.QueryVisualizations WHERE AppId = @app;
    SELECT * INTO demo.Dashboards          FROM dbo.Dashboards          WHERE AppId = @app;
    SELECT * INTO demo.DashboardTiles      FROM dbo.DashboardTiles      WHERE AppId = @app;
    COMMIT;

    SELECT (SELECT COUNT(*) FROM demo.SavedQueries) AS Queries, (SELECT COUNT(*) FROM demo.QueryVisualizations) AS Charts,
           (SELECT COUNT(*) FROM demo.Dashboards) AS Dashboards, (SELECT COUNT(*) FROM demo.DashboardTiles) AS Tiles;
END
GO

-- EXECUTE AS OWNER: the column lists are built at run time (so a hub upgrade that adds columns doesn't break the reset),
-- and dynamic SQL breaks ownership chaining, so demo_reset would otherwise need table rights of its own.
CREATE OR ALTER PROCEDURE demo.ResetNorthwind
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @app uniqueidentifier = (SELECT Id FROM dbo.Apps WHERE [Key] = N'northwind');
    IF @app IS NULL THROW 50001, 'App "northwind" not found.', 1;
    IF OBJECT_ID('demo.Dashboards') IS NULL THROW 50003, 'No starting set captured: run demo.CaptureNorthwindBaseline.', 1;

    -- One reset at a time; a second caller waits for the first to finish.
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock = sp_getapplock @Resource = 'demo.ResetNorthwind', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
    IF @lock < 0 THROW 50004, 'Another reset is running.', 1;

    -- Children first; the cascades would cover most of this, but explicit deletes keep it independent of FK settings.
    DELETE FROM dbo.DashboardShares     WHERE AppId = @app;
    DELETE FROM dbo.DashboardTiles      WHERE AppId = @app;
    DELETE FROM dbo.Dashboards          WHERE AppId = @app;
    DELETE FROM dbo.QueryShares         WHERE AppId = @app;
    DELETE FROM dbo.QueryVisualizations WHERE AppId = @app;
    DELETE FROM dbo.SavedQueries        WHERE AppId = @app;

    DECLARE @t sysname, @cols nvarchar(max), @sql nvarchar(max);
    DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
        SELECT name FROM (VALUES (1, N'SavedQueries'), (2, N'QueryVisualizations'), (3, N'Dashboards'), (4, N'DashboardTiles')) AS v(o, name) ORDER BY o;
    OPEN tables;
    FETCH NEXT FROM tables INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Only columns present in both copies: a column added by a later hub upgrade takes its default.
        SELECT @cols = STRING_AGG(QUOTENAME(c.name), N',') WITHIN GROUP (ORDER BY c.column_id)
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'demo.' + @t)
          AND EXISTS (SELECT 1 FROM sys.columns d WHERE d.object_id = OBJECT_ID(N'dbo.' + @t) AND d.name = c.name AND d.is_computed = 0);
        SET @sql = N'INSERT INTO dbo.' + QUOTENAME(@t) + N' (' + @cols + N') SELECT ' + @cols + N' FROM demo.' + QUOTENAME(@t)
                 + N' WHERE AppId = @app;';
        EXEC sp_executesql @sql, N'@app uniqueidentifier', @app = @app;
        FETCH NEXT FROM tables INTO @t;
    END
    CLOSE tables;
    DEALLOCATE tables;
    COMMIT;

    SELECT (SELECT COUNT(*) FROM dbo.SavedQueries WHERE AppId = @app) AS Queries,
           (SELECT COUNT(*) FROM dbo.Dashboards WHERE AppId = @app) AS Dashboards;
END
GO

IF USER_ID('demo_reset') IS NULL
    CREATE USER demo_reset WITH PASSWORD = '$(ResetPassword)';
ELSE
    ALTER USER demo_reset WITH PASSWORD = '$(ResetPassword)';
GRANT EXECUTE ON OBJECT::demo.ResetNorthwind TO demo_reset;
GO

EXEC demo.CaptureNorthwindBaseline;
GO
