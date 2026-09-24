/* Composite FK (RoleId, InstitutionId) guarantees an institution role can only be assigned inside that institution */
CREATE TABLE auth.UserRoles (
    Id            INT          IDENTITY(1,1) PRIMARY KEY,
    UserId        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    RoleId        INT          NOT NULL,
    InstitutionId INT          NULL REFERENCES org.Institutions(Id),
    AssignedBy    UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    ValidFrom     DATETIME2(3) NULL,
    ValidTo       DATETIME2(3) NULL,
    IsActive      BIT          NOT NULL DEFAULT 1,
    SysStartTime  DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    SysEndTime    DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),
    CONSTRAINT FK_UserRoles_RoleTenant FOREIGN KEY (RoleId, InstitutionId) REFERENCES auth.Roles(Id, InstitutionId),
    CONSTRAINT FK_UserRoles_Role FOREIGN KEY (RoleId) REFERENCES auth.Roles(Id),
    CONSTRAINT CK_UserRoles_Validity CHECK (ValidTo IS NULL OR ValidFrom IS NULL OR ValidTo > ValidFrom)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.UserRolesHistory));
GO

CREATE UNIQUE INDEX UX_UserRoles ON auth.UserRoles(UserId, RoleId) WHERE IsActive = 1;
GO
