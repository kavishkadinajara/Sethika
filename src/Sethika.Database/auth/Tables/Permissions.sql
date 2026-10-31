CREATE TABLE auth.Permissions (
    Id              INT           IDENTITY(1,1) PRIMARY KEY,
    Code            VARCHAR(150)  NOT NULL UNIQUE,
    ScreenId        SMALLINT      NOT NULL REFERENCES auth.Screens(Id),
    ScreenElementId INT           NULL REFERENCES auth.ScreenElements(Id),
    ActionId        TINYINT       NOT NULL REFERENCES auth.PermissionActions(Id),
    Scope           VARCHAR(20)   NOT NULL
        CONSTRAINT CK_Permissions_Scope CHECK (Scope = 'Platform' OR Scope = 'Institution' OR Scope = 'Self'),
    Name            NVARCHAR(150) NOT NULL,
    Description     NVARCHAR(300) NULL,
    IsSensitive     BIT           NOT NULL DEFAULT 0,
    RequiresMfa     BIT           NOT NULL DEFAULT 0,   -- step-up: re-verify before approving / voiding
    IsActive        BIT           NOT NULL DEFAULT 1,
    -- Target of auth.RoleLevelPermissions' composite FK, so a ceiling knows each permission's real scope.
    CONSTRAINT UQ_Permissions_IdScope UNIQUE (Id, Scope)
);
GO

CREATE UNIQUE INDEX UX_Permissions_Target ON auth.Permissions(ScreenId, ScreenElementId, ActionId);
GO
