CREATE TABLE auth.Roles (
    Id                INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId     INT           NULL REFERENCES org.Institutions(Id),
    Scope             VARCHAR(20)   NOT NULL
        CONSTRAINT CK_Roles_Scope CHECK (Scope = 'Platform' OR Scope = 'Institution' OR Scope = 'Donor'),
    RoleLevelId       TINYINT       NOT NULL,
    Code              VARCHAR(50)   NOT NULL,
    Name              NVARCHAR(100) NOT NULL,
    Description       NVARCHAR(300) NULL,
    IsTemplate        BIT           NOT NULL DEFAULT 0,
    TemplateRoleId    INT           NULL REFERENCES auth.Roles(Id),
    InstitutionTypeId SMALLINT      NULL REFERENCES org.InstitutionTypes(Id),
    IsSystem          BIT           NOT NULL DEFAULT 0,
    IsActive          BIT           NOT NULL DEFAULT 1,
    CreatedBy         UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    UpdatedBy         UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    SysStartTime      DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    SysEndTime        DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),
    CONSTRAINT UQ_Roles_Tenant UNIQUE (Id, InstitutionId),
    -- Composite, so the level is always one of the role's own scope: no Institution role at a Platform level.
    CONSTRAINT FK_Roles_Level FOREIGN KEY (RoleLevelId, Scope) REFERENCES auth.RoleLevels(Id, Scope),
    CONSTRAINT CK_Roles_ScopeInstitution CHECK (
        ((Scope = 'Platform' OR Scope = 'Donor') AND InstitutionId IS NULL AND IsTemplate = 0) OR
        (Scope = 'Institution' AND ((IsTemplate = 1 AND InstitutionId IS NULL) OR (IsTemplate = 0 AND InstitutionId IS NOT NULL))))
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.RolesHistory));
GO

CREATE UNIQUE INDEX UX_Roles_Code_Global ON auth.Roles(Code) WHERE InstitutionId IS NULL;
GO

CREATE UNIQUE INDEX UX_Roles_Code_Inst   ON auth.Roles(InstitutionId, Code) WHERE InstitutionId IS NOT NULL;
GO
