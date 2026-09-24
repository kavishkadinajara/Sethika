-- History table for system-versioned auth.UserRoles (maintained by SQL Server)
CREATE TABLE auth.UserRolesHistory (
    [Id] INT NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] INT NOT NULL,
    [InstitutionId] INT NULL,
    [AssignedBy] UNIQUEIDENTIFIER NULL,
    [ValidFrom] DATETIME2(3) NULL,
    [ValidTo] DATETIME2(3) NULL,
    [IsActive] BIT NOT NULL,
    [SysStartTime] DATETIME2(3) NOT NULL,
    [SysEndTime] DATETIME2(3) NOT NULL
);
GO

CREATE CLUSTERED INDEX IX_UserRolesHistory ON auth.UserRolesHistory(SysEndTime, SysStartTime);
GO
