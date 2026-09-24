-- History table for system-versioned org.InstitutionModules (maintained by SQL Server)
CREATE TABLE org.InstitutionModulesHistory (
    [InstitutionId] INT NOT NULL,
    [ModuleId] SMALLINT NOT NULL,
    [IsEnabled] BIT NOT NULL,
    [Source] VARCHAR(10) NOT NULL,
    [ChangedBy] UNIQUEIDENTIFIER NOT NULL,
    [ChangeReason] NVARCHAR(300) NULL,
    [SysStartTime] DATETIME2(3) NOT NULL,
    [SysEndTime] DATETIME2(3) NOT NULL
);
GO

CREATE CLUSTERED INDEX IX_InstitutionModulesHistory ON org.InstitutionModulesHistory(SysEndTime, SysStartTime);
GO
