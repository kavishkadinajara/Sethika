CREATE TABLE auth.UserPermissionOverrides (
    Id            INT          IDENTITY(1,1) PRIMARY KEY,
    UserId        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InstitutionId INT          NULL REFERENCES org.Institutions(Id),
    PermissionId  INT          NOT NULL REFERENCES auth.Permissions(Id),
    Effect        VARCHAR(5)   NOT NULL
        CONSTRAINT CK_UserPermOverride_Effect CHECK (Effect = 'Grant' OR Effect = 'Deny'),
    Reason        NVARCHAR(300) NOT NULL,
    ValidTo       DATETIME2(3) NULL,
    CreatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    SysStartTime  DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    SysEndTime    DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.UserPermissionOverridesHistory));
GO

CREATE UNIQUE INDEX UX_UPO ON auth.UserPermissionOverrides(UserId, InstitutionId, PermissionId);
GO
