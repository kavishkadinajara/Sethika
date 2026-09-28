/* A level's ceiling (plan A15): the permissions any role at that level may hold. A role grant counts
   in auth.vw_EffectivePermissions only when it is inside its role's level's ceiling, so a
   RolePermissions row outside it - from a buggy screen, a hand-run script, or a ceiling narrowed
   later - is simply not effective.

   Both scopes are carried and pinned by composite FK, so they are always the real ones, and
   CK_RoleLevelPermissions_Scope turns "an institution level never holds a Platform-scope
   permission" (CLAUDE.md) into a constraint: a permission is either in the level's own scope or
   is a Self permission, which any level may hold.

   Temporal, like RolePermissions (D7): narrowing a ceiling changes what users can do, so "who could
   do X on date Y" needs this history too. Platform-defined reference data, seeded by MERGE.     */
CREATE TABLE auth.RoleLevelPermissions (
    RoleLevelId     TINYINT      NOT NULL,
    LevelScope      VARCHAR(20)  NOT NULL,
    PermissionId    INT          NOT NULL,
    PermissionScope VARCHAR(20)  NOT NULL,
    SysStartTime    DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    SysEndTime      DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),
    CONSTRAINT PK_RoleLevelPermissions PRIMARY KEY (RoleLevelId, PermissionId),
    CONSTRAINT FK_RoleLevelPermissions_Level
        FOREIGN KEY (RoleLevelId, LevelScope) REFERENCES auth.RoleLevels(Id, Scope),
    CONSTRAINT FK_RoleLevelPermissions_Permission
        FOREIGN KEY (PermissionId, PermissionScope) REFERENCES auth.Permissions(Id, Scope),
    CONSTRAINT CK_RoleLevelPermissions_Scope CHECK (PermissionScope = 'Self' OR PermissionScope = LevelScope)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.RoleLevelPermissionsHistory));
GO
