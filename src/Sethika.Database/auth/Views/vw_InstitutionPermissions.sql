/*  The Institution-scope permissions that can take effect in each live institution: active, on an
    active screen, in a module the institution has switched on. The same module filter as
    vw_EffectivePermissions, without a user: the role rules use it to ask what a role would really
    grant here before handing it to someone ("held by the actor", CLAUDE.md).                    */
CREATE VIEW auth.vw_InstitutionPermissions AS
SELECT i.Id AS InstitutionId, p.Id AS PermissionId, p.Code AS PermissionCode
FROM org.Institutions i
JOIN org.InstitutionModules im ON im.InstitutionId = i.Id AND im.IsEnabled = 1
JOIN auth.Screens s            ON s.ModuleId = im.ModuleId AND s.IsActive = 1
JOIN auth.Permissions p        ON p.ScreenId = s.Id AND p.IsActive = 1 AND p.Scope = 'Institution'
WHERE i.Status IN ('Onboarding', 'Active');
GO
