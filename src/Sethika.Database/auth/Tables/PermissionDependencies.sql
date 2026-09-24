CREATE TABLE auth.PermissionDependencies (
    PermissionId         INT NOT NULL REFERENCES auth.Permissions(Id),
    RequiresPermissionId INT NOT NULL REFERENCES auth.Permissions(Id),
    PRIMARY KEY (PermissionId, RequiresPermissionId),
    CONSTRAINT CK_PermDep_NotSelf CHECK (PermissionId <> RequiresPermissionId)
);
GO
