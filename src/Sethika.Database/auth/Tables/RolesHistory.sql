-- History table for system-versioned auth.Roles (maintained by SQL Server)
CREATE TABLE auth.RolesHistory (
    [Id] INT NOT NULL,
    [InstitutionId] INT NULL,
    [Scope] VARCHAR(20) NOT NULL,
    [RoleLevelId] TINYINT NOT NULL,
    [Code] VARCHAR(50) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(300) NULL,
    [IsTemplate] BIT NOT NULL,
    [TemplateRoleId] INT NULL,
    [InstitutionTypeId] SMALLINT NULL,
    [IsSystem] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [SysStartTime] DATETIME2(3) NOT NULL,
    [SysEndTime] DATETIME2(3) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL
);
GO

CREATE CLUSTERED INDEX IX_RolesHistory ON auth.RolesHistory(SysEndTime, SysStartTime);
GO
