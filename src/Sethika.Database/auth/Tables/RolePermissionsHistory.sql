-- History table for system-versioned auth.RolePermissions (maintained by SQL Server)
CREATE TABLE auth.RolePermissionsHistory (
    [RoleId] INT NOT NULL,
    [PermissionId] INT NOT NULL,
    [GrantedBy] UNIQUEIDENTIFIER NULL,
    [SysStartTime] DATETIME2(3) NOT NULL,
    [SysEndTime] DATETIME2(3) NOT NULL
);
GO

CREATE CLUSTERED INDEX IX_RolePermissionsHistory ON auth.RolePermissionsHistory(SysEndTime, SysStartTime);
GO
