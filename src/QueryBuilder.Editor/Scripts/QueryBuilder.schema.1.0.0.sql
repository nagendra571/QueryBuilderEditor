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
    VALUES (N'20260920223446_InitialCreate', N'9.0.9');
END;

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
    VALUES (N'20260921010653_AddAuditLog', N'9.0.9');
END;

COMMIT;
GO

