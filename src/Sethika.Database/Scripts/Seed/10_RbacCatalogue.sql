/*  The RBAC catalogue (plan 5.5, A15): modules, screens, permissions and their dependencies, the
    institution template roles, and every level's ceiling.

    Only what a built screen or endpoint uses. The catalogue grows with the screens (CLAUDE.md):
    each new screen adds its rows here. BOOKINGS is a placeholder for the booking plan, holding the
    two codes the demonstration endpoints guard; its APPROVE is RequiresMfa so step-up has a real
    permission to protect.

    Rows are matched on Code, never on Id: Ids are IDENTITY values and differ between databases.
    A code that matches nothing stops the deploy (THROW) rather than being skipped by a JOIN, so a
    typo cannot quietly drop a permission from a ceiling.

    Three sets are defined by this file alone and synced exactly, deletions included: permission
    dependencies, template grants and ceilings - narrowing one is an edit here. Modules, screens
    and permissions are only added or updated; retiring one is IsActive = 0, because institution
    roles may still point at it.                                                               */

/* ---------------------------------------------------------------- modules and screens */

MERGE auth.Modules AS t
USING (
    SELECT p.Id AS PortalId, v.Code, v.Name, v.SortOrder, v.IsCore
    FROM (VALUES
        ('INSTITUTION', 'INSTITUTION', N'Institution profile', 10, 1),
        ('INSTITUTION', 'BOOKINGS',    N'Bookings',            20, 1),
        ('INSTITUTION', 'ACCESS',      N'Access control',      90, 1)
    ) AS v(PortalCode, Code, Name, SortOrder, IsCore)
    JOIN auth.Portals p ON p.Code = v.PortalCode
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.PortalId <> s.PortalId OR t.Name <> s.Name OR t.SortOrder <> s.SortOrder OR t.IsCore <> s.IsCore OR t.IsActive = 0)
    THEN UPDATE SET PortalId = s.PortalId, Name = s.Name, SortOrder = s.SortOrder, IsCore = s.IsCore, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (PortalId, Code, Name, SortOrder, IsCore, IsActive)
    VALUES (s.PortalId, s.Code, s.Name, s.SortOrder, s.IsCore, 1);

MERGE auth.Screens AS t
USING (
    SELECT m.Id AS ModuleId, v.Code, v.Name, v.Route, v.SortOrder
    FROM (VALUES
        ('INSTITUTION', 'INSTITUTION.PROFILE', N'Institution profile',   '/institution/profile', 1),
        ('BOOKINGS',    'BOOKINGS.CALENDAR',   N'Booking calendar',      '/bookings/calendar',   1),
        ('ACCESS',      'ACCESS.ROLES',        N'Roles',                 '/access/roles',        1),
        ('ACCESS',      'ACCESS.MEMBERS',      N'Staff and their roles', '/access/members',      2)
    ) AS v(ModuleCode, Code, Name, Route, SortOrder)
    JOIN auth.Modules m ON m.Code = v.ModuleCode
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.ModuleId <> s.ModuleId OR t.Name <> s.Name OR t.Route <> s.Route OR t.SortOrder <> s.SortOrder OR t.IsActive = 0)
    THEN UPDATE SET ModuleId = s.ModuleId, Name = s.Name, Route = s.Route, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (ModuleId, Code, Name, Route, IsMenuItem, SortOrder, IsActive)
    VALUES (s.ModuleId, s.Code, s.Name, s.Route, 1, s.SortOrder, 1);

/* ---------------------------------------------------------------- permissions */

DECLARE @RbacPermissions TABLE (
    Code        VARCHAR(150)  NOT NULL PRIMARY KEY,
    ScreenCode  VARCHAR(80)   NOT NULL,
    ActionCode  VARCHAR(20)   NOT NULL,
    Scope       VARCHAR(20)   NOT NULL,
    Name        NVARCHAR(150) NOT NULL,
    IsSensitive BIT           NOT NULL,
    RequiresMfa BIT           NOT NULL);

INSERT @RbacPermissions (Code, ScreenCode, ActionCode, Scope, Name, IsSensitive, RequiresMfa)
VALUES
    ('INSTITUTION.PROFILE.VIEW',  'INSTITUTION.PROFILE', 'VIEW',    'Institution', N'View the institution profile', 0, 0),
    ('INSTITUTION.PROFILE.EDIT',  'INSTITUTION.PROFILE', 'EDIT',    'Institution', N'Edit the institution profile', 1, 0),
    ('BOOKINGS.CALENDAR.VIEW',    'BOOKINGS.CALENDAR',   'VIEW',    'Institution', N'View the booking calendar',    0, 0),
    ('BOOKINGS.CALENDAR.APPROVE', 'BOOKINGS.CALENDAR',   'APPROVE', 'Institution', N'Approve bookings',             0, 1),
    ('ACCESS.ROLES.VIEW',         'ACCESS.ROLES',        'VIEW',    'Institution', N'View roles',                   0, 0),
    ('ACCESS.ROLES.CREATE',       'ACCESS.ROLES',        'CREATE',  'Institution', N'Create roles',                 1, 0),
    ('ACCESS.ROLES.EDIT',         'ACCESS.ROLES',        'EDIT',    'Institution', N'Change what a role can do',    1, 1),
    ('ACCESS.MEMBERS.VIEW',       'ACCESS.MEMBERS',      'VIEW',    'Institution', N'View staff and their roles',   0, 0),
    ('ACCESS.MEMBERS.EDIT',       'ACCESS.MEMBERS',      'EDIT',    'Institution', N'Assign and revoke roles',      1, 1);

IF EXISTS (SELECT 1 FROM @RbacPermissions WHERE Code <> ScreenCode + '.' + ActionCode)
    THROW 51001, '10_RbacCatalogue: a permission code is not <screen>.<action>.', 1;

IF EXISTS (SELECT 1 FROM @RbacPermissions r
           WHERE NOT EXISTS (SELECT 1 FROM auth.Screens s WHERE s.Code = r.ScreenCode)
              OR NOT EXISTS (SELECT 1 FROM auth.PermissionActions a WHERE a.Code = r.ActionCode))
    THROW 51002, '10_RbacCatalogue: a permission names an unknown screen or action.', 1;

MERGE auth.Permissions AS t
USING (
    SELECT r.Code, s.Id AS ScreenId, a.Id AS ActionId, r.Scope, r.Name, r.IsSensitive, r.RequiresMfa
    FROM @RbacPermissions r
    JOIN auth.Screens s           ON s.Code = r.ScreenCode
    JOIN auth.PermissionActions a ON a.Code = r.ActionCode
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.ScreenId <> s.ScreenId OR t.ScreenElementId IS NOT NULL OR t.ActionId <> s.ActionId
                  OR t.Scope <> s.Scope OR t.Name <> s.Name OR t.IsSensitive <> s.IsSensitive
                  OR t.RequiresMfa <> s.RequiresMfa OR t.IsActive = 0)
    THEN UPDATE SET ScreenId = s.ScreenId, ScreenElementId = NULL, ActionId = s.ActionId, Scope = s.Scope,
                    Name = s.Name, IsSensitive = s.IsSensitive, RequiresMfa = s.RequiresMfa, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (Code, ScreenId, ScreenElementId, ActionId, Scope, Name, IsSensitive, RequiresMfa, IsActive)
    VALUES (s.Code, s.ScreenId, NULL, s.ActionId, s.Scope, s.Name, s.IsSensitive, s.RequiresMfa, 1);

/*  A permission that is useless without another: every action on a screen needs its VIEW, and
    assigning roles needs to see them. Metadata for the role editor (5.9), which adds the
    required permission along with the one chosen.                                             */
DECLARE @RbacDependencies TABLE (Code VARCHAR(150) NOT NULL, RequiresCode VARCHAR(150) NOT NULL);

INSERT @RbacDependencies (Code, RequiresCode)
VALUES
    ('INSTITUTION.PROFILE.EDIT',  'INSTITUTION.PROFILE.VIEW'),
    ('BOOKINGS.CALENDAR.APPROVE', 'BOOKINGS.CALENDAR.VIEW'),
    ('ACCESS.ROLES.CREATE',       'ACCESS.ROLES.VIEW'),
    ('ACCESS.ROLES.EDIT',         'ACCESS.ROLES.VIEW'),
    ('ACCESS.MEMBERS.EDIT',       'ACCESS.MEMBERS.VIEW'),
    ('ACCESS.MEMBERS.EDIT',       'ACCESS.ROLES.VIEW');

IF EXISTS (SELECT 1 FROM @RbacDependencies d
           WHERE NOT EXISTS (SELECT 1 FROM auth.Permissions p WHERE p.Code = d.Code)
              OR NOT EXISTS (SELECT 1 FROM auth.Permissions p WHERE p.Code = d.RequiresCode))
    THROW 51003, '10_RbacCatalogue: a dependency names an unknown permission.', 1;

MERGE auth.PermissionDependencies AS t
USING (
    SELECT p.Id AS PermissionId, r.Id AS RequiresPermissionId
    FROM @RbacDependencies d
    JOIN auth.Permissions p ON p.Code = d.Code
    JOIN auth.Permissions r ON r.Code = d.RequiresCode
) AS s
ON t.PermissionId = s.PermissionId AND t.RequiresPermissionId = s.RequiresPermissionId
WHEN NOT MATCHED BY TARGET THEN INSERT (PermissionId, RequiresPermissionId) VALUES (s.PermissionId, s.RequiresPermissionId)
WHEN NOT MATCHED BY SOURCE THEN DELETE;

/* ---------------------------------------------------------------- ceilings */

/*  What any role at each level may ever hold (A15). OWNER and MANAGER may hold every institution
    permission - rank, not the ceiling, is what stops a manager managing managers. STAFF may see
    and do the day's work, but never manage access or change the institution's profile - nor
    see the access screens, which list other staff and their phone numbers. The platform and
    donor levels are empty until their screens exist.                                           */
DECLARE @RbacCeilings TABLE (LevelCode VARCHAR(30) NOT NULL, PermissionCode VARCHAR(150) NOT NULL);

INSERT @RbacCeilings (LevelCode, PermissionCode)
SELECT l.LevelCode, p.Code
FROM (VALUES ('OWNER'), ('MANAGER')) AS l(LevelCode)
CROSS JOIN @RbacPermissions p
WHERE p.Scope = 'Institution'
UNION ALL
SELECT 'STAFF', v.Code
FROM (VALUES
    ('INSTITUTION.PROFILE.VIEW'),
    ('BOOKINGS.CALENDAR.VIEW'),
    ('BOOKINGS.CALENDAR.APPROVE')
) AS v(Code);

IF EXISTS (SELECT 1 FROM @RbacCeilings c
           WHERE NOT EXISTS (SELECT 1 FROM auth.RoleLevels l WHERE l.Code = c.LevelCode)
              OR NOT EXISTS (SELECT 1 FROM auth.Permissions p WHERE p.Code = c.PermissionCode))
    THROW 51004, '10_RbacCatalogue: a ceiling names an unknown level or permission.', 1;

MERGE auth.RoleLevelPermissions AS t
USING (
    SELECT l.Id AS RoleLevelId, l.Scope AS LevelScope, p.Id AS PermissionId, p.Scope AS PermissionScope
    FROM @RbacCeilings c
    JOIN auth.RoleLevels l  ON l.Code = c.LevelCode
    JOIN auth.Permissions p ON p.Code = c.PermissionCode
) AS s
ON t.RoleLevelId = s.RoleLevelId AND t.PermissionId = s.PermissionId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (RoleLevelId, LevelScope, PermissionId, PermissionScope)
    VALUES (s.RoleLevelId, s.LevelScope, s.PermissionId, s.PermissionScope)
WHEN NOT MATCHED BY SOURCE THEN DELETE;

/* ---------------------------------------------------------------- template roles */

/*  Cloned into each institution when onboarding approves it (the onboarding plan), one per
    institution level and named for it. System roles: the platform defines them, and the test
    fixture's reset leaves them alone.                                                         */
WITH GlobalRoles AS (
    SELECT InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, IsSystem, IsActive
    FROM auth.Roles
    WHERE InstitutionId IS NULL
)
MERGE GlobalRoles AS t
USING (
    SELECT l.Id AS RoleLevelId, l.Code, v.Name
    FROM (VALUES
        ('OWNER',   N'Owner'),
        ('MANAGER', N'Manager'),
        ('STAFF',   N'Staff')
    ) AS v(Code, Name)
    JOIN auth.RoleLevels l ON l.Code = v.Code AND l.Scope = 'Institution'
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR t.RoleLevelId <> s.RoleLevelId OR t.IsSystem = 0 OR t.IsTemplate = 0)
    THEN UPDATE SET Name = s.Name, RoleLevelId = s.RoleLevelId, IsSystem = 1, IsTemplate = 1
WHEN NOT MATCHED THEN
    INSERT (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, IsSystem, IsActive)
    VALUES (NULL, 'Institution', s.RoleLevelId, s.Code, s.Name, 1, 1, 1);

/*  What each template grants by default: the owner everything, the manager everything but the
    profile, staff the calendar and the profile to read. Each must be inside its level's
    ceiling, or the view would drop it without a word - so that is checked, not trusted.       */
DECLARE @RbacTemplateGrants TABLE (RoleCode VARCHAR(50) NOT NULL, PermissionCode VARCHAR(150) NOT NULL);

INSERT @RbacTemplateGrants (RoleCode, PermissionCode)
SELECT 'OWNER', p.Code FROM @RbacPermissions p WHERE p.Scope = 'Institution'
UNION ALL
SELECT 'MANAGER', p.Code FROM @RbacPermissions p WHERE p.Scope = 'Institution' AND p.Code <> 'INSTITUTION.PROFILE.EDIT'
UNION ALL
SELECT 'STAFF', v.Code FROM (VALUES ('INSTITUTION.PROFILE.VIEW'), ('BOOKINGS.CALENDAR.VIEW')) AS v(Code);

IF EXISTS (SELECT 1 FROM @RbacTemplateGrants g
           WHERE NOT EXISTS (SELECT 1 FROM @RbacCeilings c WHERE c.LevelCode = g.RoleCode AND c.PermissionCode = g.PermissionCode))
    THROW 51005, '10_RbacCatalogue: a template grant is outside its level''s ceiling.', 1;

WITH TemplateGrants AS (
    SELECT rp.RoleId, rp.PermissionId
    FROM auth.RolePermissions rp
    WHERE rp.RoleId IN (SELECT r.Id FROM auth.Roles r WHERE r.IsTemplate = 1 AND r.InstitutionId IS NULL)
)
MERGE TemplateGrants AS t
USING (
    SELECT r.Id AS RoleId, p.Id AS PermissionId
    FROM @RbacTemplateGrants g
    JOIN auth.Roles r       ON r.Code = g.RoleCode AND r.IsTemplate = 1 AND r.InstitutionId IS NULL
    JOIN auth.Permissions p ON p.Code = g.PermissionCode
) AS s
ON t.RoleId = s.RoleId AND t.PermissionId = s.PermissionId
WHEN NOT MATCHED BY TARGET THEN INSERT (RoleId, PermissionId) VALUES (s.RoleId, s.PermissionId)
WHEN NOT MATCHED BY SOURCE THEN DELETE;
