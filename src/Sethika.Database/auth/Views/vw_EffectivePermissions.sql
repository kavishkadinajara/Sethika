CREATE VIEW auth.vw_EffectivePermissions AS
WITH RoleGrants AS (
    SELECT ur.UserId, ur.InstitutionId, rp.PermissionId
    FROM auth.UserRoles ur
    JOIN auth.Roles r            ON r.Id = ur.RoleId AND r.IsActive = 1
    JOIN auth.RolePermissions rp ON rp.RoleId = r.Id
    -- The ceiling (plan A15): a grant outside its role's level's ceiling is not effective, however
    -- the RolePermissions row got there.
    JOIN auth.RoleLevelPermissions rlp ON rlp.RoleLevelId = r.RoleLevelId AND rlp.PermissionId = rp.PermissionId
    WHERE ur.IsActive = 1
      AND (ur.ValidFrom IS NULL OR ur.ValidFrom <= SYSUTCDATETIME())
      AND (ur.ValidTo   IS NULL OR ur.ValidTo   >  SYSUTCDATETIME())
),
Overrides AS (
    SELECT UserId, InstitutionId, PermissionId, Effect
    FROM auth.UserPermissionOverrides
    WHERE ValidTo IS NULL OR ValidTo > SYSUTCDATETIME()
),
Candidates AS (
    SELECT UserId, InstitutionId, PermissionId FROM RoleGrants
    UNION
    SELECT UserId, InstitutionId, PermissionId FROM Overrides WHERE Effect = 'Grant'
)
SELECT c.UserId, c.InstitutionId, p.Id AS PermissionId, p.Code AS PermissionCode, p.RequiresMfa
FROM Candidates c
JOIN auth.Permissions p ON p.Id = c.PermissionId AND p.IsActive = 1
JOIN auth.Screens s     ON s.Id = p.ScreenId AND s.IsActive = 1
WHERE (c.InstitutionId IS NULL OR EXISTS (
          SELECT 1 FROM org.InstitutionModules im
          JOIN org.Institutions i ON i.Id = im.InstitutionId AND i.Status IN ('Onboarding','Active')
          WHERE im.InstitutionId = c.InstitutionId AND im.ModuleId = s.ModuleId AND im.IsEnabled = 1))
  AND NOT EXISTS (
          SELECT 1 FROM Overrides o
          WHERE o.UserId = c.UserId AND o.PermissionId = c.PermissionId AND o.Effect = 'Deny'
            AND (o.InstitutionId = c.InstitutionId OR (o.InstitutionId IS NULL AND c.InstitutionId IS NULL)));
GO
