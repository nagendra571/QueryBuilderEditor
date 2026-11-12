IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE TABLE [DataSources] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Provider] int NOT NULL,
        [ConnectionStringName] nvarchar(200) NOT NULL,
        [AllowedSchemas] nvarchar(max) NOT NULL,
        [ViewsOnly] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_DataSources] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE TABLE [SavedQueries] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [DataSourceId] uniqueidentifier NOT NULL,
        [OwnerId] nvarchar(450) NOT NULL,
        [OwnerName] nvarchar(256) NOT NULL,
        [DefinitionJson] nvarchar(max) NOT NULL,
        [IsFavorite] bit NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_SavedQueries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SavedQueries_DataSources_DataSourceId] FOREIGN KEY ([DataSourceId]) REFERENCES [DataSources] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE TABLE [QueryShares] (
        [Id] uniqueidentifier NOT NULL,
        [SavedQueryId] uniqueidentifier NOT NULL,
        [SharedWithUserId] nvarchar(450) NOT NULL,
        [SharedWithUserEmail] nvarchar(256) NOT NULL,
        [AccessLevel] int NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_QueryShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QueryShares_SavedQueries_SavedQueryId] FOREIGN KEY ([SavedQueryId]) REFERENCES [SavedQueries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_QueryShares_SavedQueryId_SharedWithUserId] ON [QueryShares] ([SavedQueryId], [SharedWithUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_DataSourceId] ON [SavedQueries] ([DataSourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_OwnerId] ON [SavedQueries] ([OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_OwnerId_Name] ON [SavedQueries] ([OwnerId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920223446_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920223446_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921010653_AddAuditLog'
)
BEGIN
    CREATE TABLE [AuditLogEntries] (
        [Id] uniqueidentifier NOT NULL,
        [TimestampUtc] datetimeoffset NOT NULL,
        [Actor] nvarchar(450) NOT NULL,
        [Action] int NOT NULL,
        [EntityType] nvarchar(100) NOT NULL,
        [EntityId] uniqueidentifier NULL,
        [EntityName] nvarchar(200) NULL,
        [DataSourceId] uniqueidentifier NULL,
        [Summary] nvarchar(500) NOT NULL,
        [DetailsJson] nvarchar(max) NULL,
        [IpAddress] nvarchar(64) NULL,
        CONSTRAINT [PK_AuditLogEntries] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921010653_AddAuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditLogEntries_Actor] ON [AuditLogEntries] ([Actor]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921010653_AddAuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditLogEntries_EntityType_EntityId] ON [AuditLogEntries] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921010653_AddAuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditLogEntries_TimestampUtc] ON [AuditLogEntries] ([TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921010653_AddAuditLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260921010653_AddAuditLog', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922164327_AddQueryDisabled'
)
BEGIN
    ALTER TABLE [SavedQueries] ADD [IsDisabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922164327_AddQueryDisabled'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922164327_AddQueryDisabled', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922184156_AddCatalogScopeAndAllowedObjects'
)
BEGIN
    ALTER TABLE [DataSources] ADD [CatalogScope] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922184156_AddCatalogScopeAndAllowedObjects'
)
BEGIN
    EXEC(N'UPDATE DataSources SET CatalogScope = CASE WHEN ViewsOnly = 1 THEN 0 ELSE 2 END')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922184156_AddCatalogScopeAndAllowedObjects'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DataSources]') AND [c].[name] = N'ViewsOnly');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [DataSources] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [DataSources] DROP COLUMN [ViewsOnly];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922184156_AddCatalogScopeAndAllowedObjects'
)
BEGIN
    ALTER TABLE [DataSources] ADD [AllowedObjects] nvarchar(max) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922184156_AddCatalogScopeAndAllowedObjects'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922184156_AddCatalogScopeAndAllowedObjects', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923015436_AddDataScopeRules'
)
BEGIN
    CREATE TABLE [DataScopeRules] (
        [Id] uniqueidentifier NOT NULL,
        [DataSourceId] uniqueidentifier NOT NULL,
        [ObjectName] nvarchar(256) NOT NULL,
        [ScopeKey] nvarchar(128) NULL,
        [ColumnName] nvarchar(128) NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_DataScopeRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DataScopeRules_DataSources_DataSourceId] FOREIGN KEY ([DataSourceId]) REFERENCES [DataSources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923015436_AddDataScopeRules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DataScopeRules_DataSourceId_ObjectName_ScopeKey] ON [DataScopeRules] ([DataSourceId], [ObjectName], [ScopeKey]) WHERE [ScopeKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923015436_AddDataScopeRules'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923015436_AddDataScopeRules', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924191229_AddDataSourceMaxRecords'
)
BEGIN
    ALTER TABLE [DataSources] ADD [MaxRecords] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924191229_AddDataSourceMaxRecords'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924191229_AddDataSourceMaxRecords', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103040624_AddQueryVisualizations'
)
BEGIN
    CREATE TABLE [QueryVisualizations] (
        [Id] uniqueidentifier NOT NULL,
        [SavedQueryId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [OptionsJson] nvarchar(max) NOT NULL,
        [DisplayOrder] int NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_QueryVisualizations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QueryVisualizations_SavedQueries_SavedQueryId] FOREIGN KEY ([SavedQueryId]) REFERENCES [SavedQueries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103040624_AddQueryVisualizations'
)
BEGIN
    CREATE INDEX [IX_QueryVisualizations_SavedQueryId] ON [QueryVisualizations] ([SavedQueryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103040624_AddQueryVisualizations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261103040624_AddQueryVisualizations', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE TABLE [Dashboards] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [OwnerId] nvarchar(450) NOT NULL,
        [OwnerName] nvarchar(256) NOT NULL,
        [Version] int NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_Dashboards] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE TABLE [DashboardShares] (
        [Id] uniqueidentifier NOT NULL,
        [DashboardId] uniqueidentifier NOT NULL,
        [SharedWithUserId] nvarchar(450) NOT NULL,
        [SharedWithUserEmail] nvarchar(256) NOT NULL,
        [AccessLevel] int NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_DashboardShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DashboardShares_Dashboards_DashboardId] FOREIGN KEY ([DashboardId]) REFERENCES [Dashboards] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE TABLE [DashboardTiles] (
        [Id] uniqueidentifier NOT NULL,
        [DashboardId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [VisualizationId] uniqueidentifier NULL,
        [Title] nvarchar(200) NULL,
        [Text] nvarchar(2000) NULL,
        [Order] int NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [CreatedBy] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(450) NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_DashboardTiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DashboardTiles_Dashboards_DashboardId] FOREIGN KEY ([DashboardId]) REFERENCES [Dashboards] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DashboardTiles_QueryVisualizations_VisualizationId] FOREIGN KEY ([VisualizationId]) REFERENCES [QueryVisualizations] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE INDEX [IX_Dashboards_OwnerId] ON [Dashboards] ([OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DashboardShares_DashboardId_SharedWithUserId] ON [DashboardShares] ([DashboardId], [SharedWithUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE INDEX [IX_DashboardTiles_DashboardId] ON [DashboardTiles] ([DashboardId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    CREATE INDEX [IX_DashboardTiles_VisualizationId] ON [DashboardTiles] ([VisualizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103044216_AddDashboards'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261103044216_AddDashboards', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103235143_AddDashboardGridPositions'
)
BEGIN
    ALTER TABLE [DashboardTiles] ADD [AutoHeight] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103235143_AddDashboardGridPositions'
)
BEGIN
    ALTER TABLE [DashboardTiles] ADD [X] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103235143_AddDashboardGridPositions'
)
BEGIN
    ALTER TABLE [DashboardTiles] ADD [Y] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103235143_AddDashboardGridPositions'
)
BEGIN
    ALTER TABLE [Dashboards] ADD [GridVersion] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103235143_AddDashboardGridPositions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261103235143_AddDashboardGridPositions', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104005823_AddDashboardParameters'
)
BEGIN
    ALTER TABLE [DashboardTiles] ADD [ParameterMappingsJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104005823_AddDashboardParameters'
)
BEGIN
    ALTER TABLE [Dashboards] ADD [ParametersJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104005823_AddDashboardParameters'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104005823_AddDashboardParameters', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106221018_AddDataSourceManagedConnection'
)
BEGIN
    ALTER TABLE [DataSources] ADD [EncryptedConnection] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106221018_AddDataSourceManagedConnection'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106221018_AddDataSourceManagedConnection', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110023605_AddDataSourceScopeKeys'
)
BEGIN
    ALTER TABLE [DataSources] ADD [ScopeKeys] nvarchar(max) NOT NULL DEFAULT ('');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110023605_AddDataSourceScopeKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110023605_AddDataSourceScopeKeys', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE TABLE [Apps] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(64) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Apps] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE TABLE [MetadataSettings] (
        [Key] nvarchar(64) NOT NULL,
        [Value] nvarchar(400) NOT NULL,
        CONSTRAINT [PK_MetadataSettings] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'IsEnabled', N'CreatedAtUtc') AND [object_id] = OBJECT_ID(N'[Apps]'))
        SET IDENTITY_INSERT [Apps] ON;
    EXEC(N'INSERT INTO [Apps] ([Id], [Key], [Name], [IsEnabled], [CreatedAtUtc])
    VALUES (''00000000-0000-0000-0000-00000000000d'', N''default'', N''Default'', CAST(1 AS bit), ''2026-11-10T00:00:00.0000000+00:00'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'IsEnabled', N'CreatedAtUtc') AND [object_id] = OBJECT_ID(N'[Apps]'))
        SET IDENTITY_INSERT [Apps] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [SavedQueries] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [QueryVisualizations] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [QueryShares] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DataSources] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DataScopeRules] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DashboardTiles] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DashboardShares] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [Dashboards] ADD [AppId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-00000000000d';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [AuditLogEntries] ADD [AppId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'UPDATE [AuditLogEntries] SET [AppId] = ''00000000-0000-0000-0000-00000000000d'' WHERE [AppId] IS NULL')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_AppId_OwnerId] ON [SavedQueries] ([AppId], [OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_QueryVisualizations_AppId] ON [QueryVisualizations] ([AppId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_QueryShares_AppId] ON [QueryShares] ([AppId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_DataSources_AppId] ON [DataSources] ([AppId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_DataScopeRules_AppId] ON [DataScopeRules] ([AppId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_DashboardTiles_AppId] ON [DashboardTiles] ([AppId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_DashboardShares_AppId] ON [DashboardShares] ([AppId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_Dashboards_AppId_OwnerId] ON [Dashboards] ([AppId], [OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE INDEX [IX_AuditLogEntries_AppId_TimestampUtc] ON [AuditLogEntries] ([AppId], [TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Apps_Key] ON [Apps] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [AuditLogEntries] ADD CONSTRAINT [FK_AuditLogEntries_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [Dashboards] ADD CONSTRAINT [FK_Dashboards_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DashboardShares] ADD CONSTRAINT [FK_DashboardShares_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DashboardTiles] ADD CONSTRAINT [FK_DashboardTiles_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DataScopeRules] ADD CONSTRAINT [FK_DataScopeRules_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [DataSources] ADD CONSTRAINT [FK_DataSources_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [QueryShares] ADD CONSTRAINT [FK_QueryShares_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [QueryVisualizations] ADD CONSTRAINT [FK_QueryVisualizations_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    ALTER TABLE [SavedQueries] ADD CONSTRAINT [FK_SavedQueries_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SavedQueries]') AND [c].[name] = N'AppId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [SavedQueries] DROP CONSTRAINT ' + @var1 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[QueryVisualizations]') AND [c].[name] = N'AppId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [QueryVisualizations] DROP CONSTRAINT ' + @var2 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[QueryShares]') AND [c].[name] = N'AppId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [QueryShares] DROP CONSTRAINT ' + @var3 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DataSources]') AND [c].[name] = N'AppId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [DataSources] DROP CONSTRAINT ' + @var4 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DataScopeRules]') AND [c].[name] = N'AppId');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [DataScopeRules] DROP CONSTRAINT ' + @var5 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DashboardTiles]') AND [c].[name] = N'AppId');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [DashboardTiles] DROP CONSTRAINT ' + @var6 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DashboardShares]') AND [c].[name] = N'AppId');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [DashboardShares] DROP CONSTRAINT ' + @var7 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Dashboards]') AND [c].[name] = N'AppId');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [Dashboards] DROP CONSTRAINT ' + @var8 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [SavedQueries] ADD CONSTRAINT [DF_SavedQueries_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [QueryVisualizations] ADD CONSTRAINT [DF_QueryVisualizations_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [QueryShares] ADD CONSTRAINT [DF_QueryShares_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [DataSources] ADD CONSTRAINT [DF_DataSources_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [DataScopeRules] ADD CONSTRAINT [DF_DataScopeRules_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [DashboardTiles] ADD CONSTRAINT [DF_DashboardTiles_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [DashboardShares] ADD CONSTRAINT [DF_DashboardShares_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [Dashboards] ADD CONSTRAINT [DF_Dashboards_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    EXEC(N'ALTER TABLE [AuditLogEntries] ADD CONSTRAINT [DF_AuditLogEntries_AppId] DEFAULT ''00000000-0000-0000-0000-00000000000d'' FOR [AppId]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110171537_AddApps'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110171537_AddApps', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    ALTER TABLE [Apps] ADD [ScopeKeys] nvarchar(max) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE TABLE [AppReturnUrls] (
        [Id] uniqueidentifier NOT NULL,
        [AppId] uniqueidentifier NOT NULL,
        [Url] nvarchar(800) NOT NULL,
        CONSTRAINT [PK_AppReturnUrls] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppReturnUrls_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE TABLE [AppSigningKeys] (
        [Id] uniqueidentifier NOT NULL,
        [AppId] uniqueidentifier NOT NULL,
        [Kid] nvarchar(64) NOT NULL,
        [Algorithm] nvarchar(10) NOT NULL,
        [PublicKeyPem] nvarchar(4000) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [RevokedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_AppSigningKeys] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppSigningKeys_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE TABLE [DataProtectionKeys] (
        [Id] int NOT NULL IDENTITY,
        [FriendlyName] nvarchar(200) NOT NULL,
        [Xml] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE TABLE [LaunchReplays] (
        [Jti] nvarchar(200) NOT NULL,
        [AppId] uniqueidentifier NOT NULL,
        [ExpiresUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_LaunchReplays] PRIMARY KEY ([AppId], [Jti])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE TABLE [SeenUsers] (
        [Id] uniqueidentifier NOT NULL,
        [AppId] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Name] nvarchar(200) NULL,
        [Email] nvarchar(320) NULL,
        [LastLaunchUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SeenUsers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SeenUsers_Apps_AppId] FOREIGN KEY ([AppId]) REFERENCES [Apps] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppReturnUrls_AppId_Url] ON [AppReturnUrls] ([AppId], [Url]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppSigningKeys_AppId_Kid] ON [AppSigningKeys] ([AppId], [Kid]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE INDEX [IX_LaunchReplays_ExpiresUtc] ON [LaunchReplays] ([ExpiresUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SeenUsers_AppId_UserId] ON [SeenUsers] ([AppId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Apps]') AND [c].[name] = N'ScopeKeys');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [Apps] DROP CONSTRAINT ' + @var9 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    EXEC(N'ALTER TABLE [Apps] ADD CONSTRAINT [DF_Apps_ScopeKeys] DEFAULT '''' FOR [ScopeKeys]')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192529_AddHubRegistry'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110192529_AddHubRegistry', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111151001_AddAppSessionsValidAfter'
)
BEGIN
    ALTER TABLE [Apps] ADD [SessionsValidAfterUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111151001_AddAppSessionsValidAfter'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111151001_AddAppSessionsValidAfter', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [DataSources] ADD [LockedBySuperAdmin] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [Dashboards] ADD [PublishedAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [Dashboards] ADD [PublishedBy] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [Dashboards] ADD [PublishedToApp] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [Apps] ADD [AppAdminsCanManageConnections] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [Apps] ADD [MaxRecords] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    ALTER TABLE [Apps] ADD [Version] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111204157_AddHubRolesAndPublishing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111204157_AddHubRolesAndPublishing', N'10.0.14');
END;

COMMIT;
GO

