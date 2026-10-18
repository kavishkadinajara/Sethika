-- History table for system-versioned auth.RoleLevelPermissions (maintained by SQL Server)
CREATE TABLE auth.RoleLevelPermissionsHistory (
    [RoleLevelId] TINYINT NOT NULL,
    [LevelScope] VARCHAR(20) NOT NULL,
    [PermissionId] INT NOT NULL,
    [PermissionScope] VARCHAR(20) NOT NULL,
    [SysStartTime] DATETIME2(3) NOT NULL,
    [SysEndTime] DATETIME2(3) NOT NULL
);
GO

CREATE CLUSTERED INDEX IX_RoleLevelPermissionsHistory ON auth.RoleLevelPermissionsHistory(SysEndTime, SysStartTime);
GO
