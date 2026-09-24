-- History table for system-versioned auth.UserPermissionOverrides (maintained by SQL Server)
CREATE TABLE auth.UserPermissionOverridesHistory (
    [Id] INT NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [InstitutionId] INT NULL,
    [PermissionId] INT NOT NULL,
    [Effect] VARCHAR(5) NOT NULL,
    [Reason] NVARCHAR(300) NOT NULL,
    [ValidTo] DATETIME2(3) NULL,
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [SysStartTime] DATETIME2(3) NOT NULL,
    [SysEndTime] DATETIME2(3) NOT NULL
);
GO

CREATE CLUSTERED INDEX IX_UserPermissionOverridesHistory ON auth.UserPermissionOverridesHistory(SysEndTime, SysStartTime);
GO
