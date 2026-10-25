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
Granted AS (
    SELECT UserId, InstitutionId, PermissionId FROM RoleGrants
    UNION
    SELECT UserId, InstitutionId, PermissionId FROM Overrides WHERE Effect = 'Grant'
),
/*  Plan 04, Y1-Y2: platform support. A platform user who holds PLATFORM.INSTITUTIONS.EXECUTE
    globally, and is not denied it, holds in every live institution every Institution-scope
    permission; the module filter below still drops what the institution has switched off. Read
    here, so the permission cache, the role rules ("held by the actor") and every other reader
    see it without C# deciding anything.                                                       */
Support AS (
    SELECT g.UserId, i.Id AS InstitutionId, p.Id AS PermissionId
    FROM Granted g
    JOIN auth.Permissions sp ON sp.Id = g.PermissionId AND sp.Code = 'PLATFORM.INSTITUTIONS.EXECUTE' AND sp.IsActive = 1
    JOIN auth.Users u        ON u.Id = g.UserId AND u.UserType = 'PlatformStaff'
    CROSS JOIN org.Institutions i
    JOIN auth.Permissions p  ON p.Scope = 'Institution' AND p.IsActive = 1
    WHERE g.InstitutionId IS NULL
      AND i.IsDeleted = 0 AND i.Status IN ('Onboarding', 'Active')
      AND NOT EXISTS (SELECT 1 FROM Overrides o
                      WHERE o.UserId = g.UserId AND o.PermissionId = sp.Id AND o.Effect = 'Deny' AND o.InstitutionId IS NULL)
),
Candidates AS (
    SELECT UserId, InstitutionId, PermissionId FROM Granted
    UNION
    SELECT UserId, InstitutionId, PermissionId FROM Support
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
