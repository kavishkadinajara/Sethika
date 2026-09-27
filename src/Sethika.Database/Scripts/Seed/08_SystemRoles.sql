/*  Roles that exist once, globally, rather than per institution.

    DONOR is held by every donor from signup. CK_Roles_ScopeInstitution requires a 'Donor' scope
    role to have no institution and IsTemplate = 0, so it is a system role, not a template.
    Its permissions arrive with the RBAC catalogue.

    The target is narrowed to global roles in a CTE rather than in ON: UX_Roles_Code_Global only
    makes Code unique where InstitutionId IS NULL, and a filter in ON would let a same-coded
    institution role be matched and overwritten.                                           */
WITH GlobalRoles AS (
    SELECT InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, IsSystem, IsActive
    FROM auth.Roles
    WHERE InstitutionId IS NULL
)
MERGE GlobalRoles AS t
USING (
    SELECT 'Donor' AS Scope, l.Id AS RoleLevelId, 'DONOR' AS Code, N'Donor' AS Name
    FROM auth.RoleLevels l
    WHERE l.Code = 'DONOR'
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR t.RoleLevelId <> s.RoleLevelId OR t.IsSystem = 0)
    THEN UPDATE SET Name = s.Name, RoleLevelId = s.RoleLevelId, IsSystem = 1
WHEN NOT MATCHED THEN
    INSERT (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, IsSystem, IsActive)
    VALUES (NULL, s.Scope, s.RoleLevelId, s.Code, s.Name, 0, 1, 1);
