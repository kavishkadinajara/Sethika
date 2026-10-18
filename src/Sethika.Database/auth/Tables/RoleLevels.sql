/* Every role sits at a level (plan A15). Rank orders seniority - 1 is the most senior - and a role
   may only be managed by someone of strictly more senior rank. The ceiling, the permissions any role
   at a level may hold, is auth.RoleLevelPermissions.
   Platform-defined reference data: seeded by MERGE, never created by institutions. */
CREATE TABLE auth.RoleLevels (
    Id       TINYINT       NOT NULL PRIMARY KEY,
    Scope    VARCHAR(20)   NOT NULL
        CONSTRAINT CK_RoleLevels_Scope CHECK (Scope = 'Platform' OR Scope = 'Institution' OR Scope = 'Donor'),
    Rank     TINYINT       NOT NULL
        CONSTRAINT CK_RoleLevels_Rank CHECK (Rank >= 1),
    Code     VARCHAR(30)   NOT NULL,
    Name     NVARCHAR(100) NOT NULL,
    IsActive BIT           NOT NULL DEFAULT 1,
    CONSTRAINT UQ_RoleLevels_Code      UNIQUE (Code),
    CONSTRAINT UQ_RoleLevels_ScopeRank UNIQUE (Scope, Rank),
    -- Target of auth.Roles' composite FK, so a role's level is always in the role's own scope.
    CONSTRAINT UQ_RoleLevels_IdScope   UNIQUE (Id, Scope)
);
GO
