CREATE TABLE auth.RolePermissions (
    RoleId       INT          NOT NULL REFERENCES auth.Roles(Id),
    PermissionId INT          NOT NULL REFERENCES auth.Permissions(Id),
    GrantedBy    UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    SysStartTime DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    SysEndTime   DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),
    PRIMARY KEY (RoleId, PermissionId)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.RolePermissionsHistory));
GO
