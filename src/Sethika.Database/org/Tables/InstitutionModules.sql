/* Temporal: full history of which modules were on/off and when */
CREATE TABLE org.InstitutionModules (
    InstitutionId  INT          NOT NULL REFERENCES org.Institutions(Id),
    ModuleId       SMALLINT     NOT NULL REFERENCES auth.Modules(Id),
    IsEnabled      BIT          NOT NULL DEFAULT 1,
    Source         VARCHAR(10)  NOT NULL DEFAULT 'Plan'
        CONSTRAINT CK_InstModule_Source CHECK (Source = 'Default' OR Source = 'Plan' OR Source = 'Manual'),
    ChangedBy      UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ChangeReason   NVARCHAR(300) NULL,
    SysStartTime   DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    SysEndTime     DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),
    PRIMARY KEY (InstitutionId, ModuleId)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = org.InstitutionModulesHistory));
GO
