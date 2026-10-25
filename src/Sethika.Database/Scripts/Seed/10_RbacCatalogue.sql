/*  The RBAC catalogue (plan 5.5, A15): modules, screens, permissions and their dependencies, the
    institution template roles, and every level's ceiling.

    Only what a built screen or endpoint uses. The catalogue grows with the screens (CLAUDE.md):
    each new screen adds its rows here. BOOKINGS and the donor's BOOK are the booking plan's
    (plan 03, 5.1): the institution's meal slots and calendar, and a donor's own bookings.
    SERVICES and the donor's REQUEST are Phase E's: religious services, the leader roster and
    appointments, and a donor's own appointment requests.

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
        -- Not IsCore: a care home has no religious leaders. On by default for the religious
        -- institution types (InstitutionTypeDefaultModules, below; Phase E, V12).
        ('INSTITUTION', 'SERVICES',    N'Religious services',  30, 0),
        ('INSTITUTION', 'ACCESS',      N'Access control',      90, 1),
        -- Not IsCore: core means "on in every institution", and these belong to no institution.
        ('DONOR',       'APPLY',       N'Register an institution', 10, 0),
        ('DONOR',       'BOOK',        N'Book a meal',             20, 0),
        ('DONOR',       'REQUEST',     N'Request a religious service', 30, 0),
        ('ADMIN',       'ONBOARDING',  N'Institution onboarding',  10, 0),
        -- Plan 04, U4: the platform's own administration.
        ('ADMIN',       'INSTITUTIONS', N'Institutions',           20, 0),
        ('ADMIN',       'USERS',        N'Users',                  30, 0)
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
        ('BOOKINGS',    'BOOKINGS.SLOTS',      N'Meal slots',            '/bookings/slots',      2),
        ('SERVICES',    'SERVICES.TYPES',        N'Religious services',    '/services/types',        1),
        ('SERVICES',    'SERVICES.LEADERS',      N'Religious leaders',     '/services/leaders',      2),
        ('SERVICES',    'SERVICES.APPOINTMENTS', N'Service appointments',  '/services/appointments', 3),
        ('SERVICES',    'SERVICES.MYSCHEDULE',   N'My schedule',           '/services/schedule',     4),
        ('ACCESS',      'ACCESS.ROLES',        N'Roles',                 '/access/roles',        1),
        ('ACCESS',      'ACCESS.MEMBERS',      N'Staff and their roles', '/access/members',      2),
        ('APPLY',       'ONBOARDING.APPLICATION', N'Institution application', '/apply',               1),
        ('BOOK',        'MYBOOKINGS.BOOKINGS',    N'My bookings',             '/bookings/mine',       1),
        ('REQUEST',     'MYSERVICES.APPOINTMENTS', N'My service requests',    '/services/mine',       1),
        ('ONBOARDING',  'ONBOARDING.REQUESTS',    N'Registration requests',   '/admin/registrations', 1),
        ('INSTITUTIONS', 'PLATFORM.INSTITUTIONS', N'All institutions',        '/admin/institutions',  1),
        ('USERS',       'PLATFORM.USERS',         N'All users',               '/admin/users',         1)
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
    /*  Bookings (plan 03). EDIT on the calendar is completing, marking a no-show and cancelling; on
        slots it also covers blocked dates, deactivating a slot and the public listing. APPROVE is
        not RequiresMfa: deciding a donor's request is the day's work, not worth an SMS code
        (plan 03, E1).                                                                          */
    ('BOOKINGS.CALENDAR.VIEW',    'BOOKINGS.CALENDAR',   'VIEW',    'Institution', N'View the booking calendar',    0, 0),
    ('BOOKINGS.CALENDAR.APPROVE', 'BOOKINGS.CALENDAR',   'APPROVE', 'Institution', N'Approve bookings',             0, 0),
    ('BOOKINGS.CALENDAR.REJECT',  'BOOKINGS.CALENDAR',   'REJECT',  'Institution', N'Reject booking requests',      0, 0),
    ('BOOKINGS.CALENDAR.EDIT',    'BOOKINGS.CALENDAR',   'EDIT',    'Institution', N'Complete, mark no-shows and cancel bookings', 0, 0),
    ('BOOKINGS.SLOTS.VIEW',       'BOOKINGS.SLOTS',      'VIEW',    'Institution', N'View meal slots and blocked dates', 0, 0),
    ('BOOKINGS.SLOTS.CREATE',     'BOOKINGS.SLOTS',      'CREATE',  'Institution', N'Create meal slots',            0, 0),
    ('BOOKINGS.SLOTS.EDIT',       'BOOKINGS.SLOTS',      'EDIT',    'Institution', N'Change meal slots and blocked dates', 0, 0),
    /*  Religious services (Phase E, section 5). APPROVE on appointments is assigning leaders, which
        approves a request; EDIT is reassigning, answering for a leader, cancelling and completing.
        MYSCHEDULE is a leader's own: EDIT is accepting or declining their own assignments.     */
    ('SERVICES.TYPES.VIEW',          'SERVICES.TYPES',        'VIEW',    'Institution', N'View the religious services offered',            0, 0),
    ('SERVICES.TYPES.CREATE',        'SERVICES.TYPES',        'CREATE',  'Institution', N'Add religious services',                         0, 0),
    ('SERVICES.TYPES.EDIT',          'SERVICES.TYPES',        'EDIT',    'Institution', N'Change religious services',                      0, 0),
    ('SERVICES.LEADERS.VIEW',        'SERVICES.LEADERS',      'VIEW',    'Institution', N'View religious leaders, their hours and leave',   0, 0),
    ('SERVICES.LEADERS.CREATE',      'SERVICES.LEADERS',      'CREATE',  'Institution', N'Add religious leaders',                          0, 0),
    ('SERVICES.LEADERS.EDIT',        'SERVICES.LEADERS',      'EDIT',    'Institution', N'Change religious leaders, their hours and leave', 0, 0),
    ('SERVICES.APPOINTMENTS.VIEW',   'SERVICES.APPOINTMENTS', 'VIEW',    'Institution', N'View service appointments',                      0, 0),
    ('SERVICES.APPOINTMENTS.APPROVE','SERVICES.APPOINTMENTS', 'APPROVE', 'Institution', N'Assign leaders and approve requests',            0, 0),
    ('SERVICES.APPOINTMENTS.REJECT', 'SERVICES.APPOINTMENTS', 'REJECT',  'Institution', N'Reject service requests',                        0, 0),
    ('SERVICES.APPOINTMENTS.EDIT',   'SERVICES.APPOINTMENTS', 'EDIT',    'Institution', N'Reassign, answer for a leader, cancel and complete appointments', 0, 0),
    ('SERVICES.MYSCHEDULE.VIEW',     'SERVICES.MYSCHEDULE',   'VIEW',    'Institution', N'See my schedule as a religious leader',          0, 0),
    ('SERVICES.MYSCHEDULE.EDIT',     'SERVICES.MYSCHEDULE',   'EDIT',    'Institution', N'Accept or decline my assignments',               0, 0),
    ('ACCESS.ROLES.VIEW',         'ACCESS.ROLES',        'VIEW',    'Institution', N'View roles',                   0, 0),
    ('ACCESS.ROLES.CREATE',       'ACCESS.ROLES',        'CREATE',  'Institution', N'Create roles',                 1, 0),
    ('ACCESS.ROLES.EDIT',         'ACCESS.ROLES',        'EDIT',    'Institution', N'Change what a role can do',    1, 1),
    ('ACCESS.MEMBERS.VIEW',       'ACCESS.MEMBERS',      'VIEW',    'Institution', N'View staff and their roles',   0, 0),
    ('ACCESS.MEMBERS.EDIT',       'ACCESS.MEMBERS',      'EDIT',    'Institution', N'Assign and revoke roles',      1, 1),
    /*  Onboarding (plan 02, B1). An applicant acts on their own application, so Self scope, which
        the donor level may hold. The platform side shows applicants' NICs, hence sensitive; a
        decision creates a tenant or ends an application for good, hence step-up.             */
    ('ONBOARDING.APPLICATION.VIEW',   'ONBOARDING.APPLICATION', 'VIEW',    'Self',     N'See my institution applications',             0, 0),
    ('ONBOARDING.APPLICATION.CREATE', 'ONBOARDING.APPLICATION', 'CREATE',  'Self',     N'Start an institution application',            0, 0),
    ('ONBOARDING.APPLICATION.EDIT',   'ONBOARDING.APPLICATION', 'EDIT',    'Self',     N'Fill in, submit and withdraw my applications', 0, 0),
    ('ONBOARDING.REQUESTS.VIEW',      'ONBOARDING.REQUESTS',    'VIEW',    'Platform', N'View registration requests',                  1, 0),
    ('ONBOARDING.REQUESTS.EDIT',      'ONBOARDING.REQUESTS',    'EDIT',    'Platform', N'Review registration requests',                1, 0),
    ('ONBOARDING.REQUESTS.APPROVE',   'ONBOARDING.REQUESTS',    'APPROVE', 'Platform', N'Approve registration requests',               1, 1),
    ('ONBOARDING.REQUESTS.REJECT',    'ONBOARDING.REQUESTS',    'REJECT',  'Platform', N'Reject registration requests',                1, 1),
    /*  Platform administration (plan 04, U4). EXECUTE on institutions is working inside one as
        platform support, with every permission there (Y1-Y3): step-up. Accounts list phone numbers,
        hence sensitive; making, suspending and giving platform roles need a step-up.           */
    ('PLATFORM.INSTITUTIONS.VIEW',    'PLATFORM.INSTITUTIONS',  'VIEW',    'Platform', N'View every institution',                      0, 0),
    ('PLATFORM.INSTITUTIONS.EXECUTE', 'PLATFORM.INSTITUTIONS',  'EXECUTE', 'Platform', N'Work inside any institution as platform support', 1, 1),
    ('PLATFORM.USERS.VIEW',           'PLATFORM.USERS',         'VIEW',    'Platform', N'View every account',                          1, 0),
    ('PLATFORM.USERS.CREATE',         'PLATFORM.USERS',         'CREATE',  'Platform', N'Make accounts for people who cannot sign up', 1, 1),
    ('PLATFORM.USERS.EDIT',           'PLATFORM.USERS',         'EDIT',    'Platform', N'Suspend accounts and give platform roles',    1, 1),
    /*  A donor's own bookings (plan 03): Self scope, like applying. EDIT is cancelling one.       */
    ('MYBOOKINGS.BOOKINGS.VIEW',      'MYBOOKINGS.BOOKINGS',    'VIEW',    'Self',     N'See my bookings',                             0, 0),
    ('MYBOOKINGS.BOOKINGS.CREATE',    'MYBOOKINGS.BOOKINGS',    'CREATE',  'Self',     N'Book a meal slot',                            0, 0),
    ('MYBOOKINGS.BOOKINGS.EDIT',      'MYBOOKINGS.BOOKINGS',    'EDIT',    'Self',     N'Cancel my bookings',                          0, 0),
    /*  A donor's own service requests (Phase E): Self scope, like bookings. EDIT is cancelling one. */
    ('MYSERVICES.APPOINTMENTS.VIEW',   'MYSERVICES.APPOINTMENTS', 'VIEW',   'Self',    N'See my service requests',                     0, 0),
    ('MYSERVICES.APPOINTMENTS.CREATE', 'MYSERVICES.APPOINTMENTS', 'CREATE', 'Self',    N'Request a religious service',                 0, 0),
    ('MYSERVICES.APPOINTMENTS.EDIT',   'MYSERVICES.APPOINTMENTS', 'EDIT',   'Self',    N'Cancel my service requests',                  0, 0);

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
    ('BOOKINGS.CALENDAR.REJECT',  'BOOKINGS.CALENDAR.VIEW'),
    ('BOOKINGS.CALENDAR.EDIT',    'BOOKINGS.CALENDAR.VIEW'),
    ('BOOKINGS.CALENDAR.VIEW',    'BOOKINGS.SLOTS.VIEW'),      -- a booking is read beside its slot
    ('BOOKINGS.SLOTS.CREATE',     'BOOKINGS.SLOTS.VIEW'),
    ('BOOKINGS.SLOTS.EDIT',       'BOOKINGS.SLOTS.VIEW'),
    ('ACCESS.ROLES.CREATE',       'ACCESS.ROLES.VIEW'),
    ('ACCESS.ROLES.EDIT',         'ACCESS.ROLES.VIEW'),
    ('ACCESS.MEMBERS.EDIT',       'ACCESS.MEMBERS.VIEW'),
    ('ACCESS.MEMBERS.EDIT',       'ACCESS.ROLES.VIEW'),
    ('ONBOARDING.APPLICATION.CREATE', 'ONBOARDING.APPLICATION.VIEW'),
    ('ONBOARDING.APPLICATION.EDIT',   'ONBOARDING.APPLICATION.VIEW'),
    ('ONBOARDING.REQUESTS.EDIT',      'ONBOARDING.REQUESTS.VIEW'),
    ('ONBOARDING.REQUESTS.APPROVE',   'ONBOARDING.REQUESTS.VIEW'),
    ('ONBOARDING.REQUESTS.REJECT',    'ONBOARDING.REQUESTS.VIEW'),
    ('PLATFORM.INSTITUTIONS.EXECUTE', 'PLATFORM.INSTITUTIONS.VIEW'),
    ('PLATFORM.USERS.CREATE',        'PLATFORM.USERS.VIEW'),
    ('PLATFORM.USERS.EDIT',          'PLATFORM.USERS.VIEW'),
    ('MYBOOKINGS.BOOKINGS.CREATE',    'MYBOOKINGS.BOOKINGS.VIEW'),
    ('MYBOOKINGS.BOOKINGS.EDIT',      'MYBOOKINGS.BOOKINGS.VIEW'),
    ('SERVICES.TYPES.CREATE',         'SERVICES.TYPES.VIEW'),
    ('SERVICES.TYPES.EDIT',           'SERVICES.TYPES.VIEW'),
    ('SERVICES.LEADERS.CREATE',       'SERVICES.LEADERS.VIEW'),
    ('SERVICES.LEADERS.EDIT',         'SERVICES.LEADERS.VIEW'),
    ('SERVICES.APPOINTMENTS.APPROVE', 'SERVICES.APPOINTMENTS.VIEW'),
    ('SERVICES.APPOINTMENTS.REJECT',  'SERVICES.APPOINTMENTS.VIEW'),
    ('SERVICES.APPOINTMENTS.EDIT',    'SERVICES.APPOINTMENTS.VIEW'),
    ('SERVICES.APPOINTMENTS.VIEW',    'SERVICES.LEADERS.VIEW'),    -- an appointment is read with its leaders
    ('SERVICES.APPOINTMENTS.VIEW',    'SERVICES.TYPES.VIEW'),
    ('SERVICES.MYSCHEDULE.EDIT',      'SERVICES.MYSCHEDULE.VIEW'),
    ('MYSERVICES.APPOINTMENTS.CREATE','MYSERVICES.APPOINTMENTS.VIEW'),
    ('MYSERVICES.APPOINTMENTS.EDIT',  'MYSERVICES.APPOINTMENTS.VIEW');

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
    see the access screens, which list other staff and their phone numbers. Deciding and closing
    bookings is the day's work; setting up the slots is not. Seeing the services, the leaders and
    the appointments is, and so is a leader's own schedule (the LEADER template is at this level);
    deciding who goes where is not. The donor level may hold what a person does for themselves
    (applying for an institution, booking a meal, requesting a service). PLATFORM_ADMIN may hold
    every platform permission; PLATFORM_STAFF may review registrations but not decide them
    (plan 02, B1).                                                                              */
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
    ('BOOKINGS.SLOTS.VIEW'),
    ('BOOKINGS.CALENDAR.VIEW'),
    ('BOOKINGS.CALENDAR.APPROVE'),
    ('BOOKINGS.CALENDAR.REJECT'),
    ('BOOKINGS.CALENDAR.EDIT'),
    ('SERVICES.TYPES.VIEW'),
    ('SERVICES.LEADERS.VIEW'),
    ('SERVICES.APPOINTMENTS.VIEW'),
    ('SERVICES.MYSCHEDULE.VIEW'),
    ('SERVICES.MYSCHEDULE.EDIT')
) AS v(Code)
UNION ALL
SELECT 'DONOR', v.Code
FROM (VALUES
    ('ONBOARDING.APPLICATION.VIEW'),
    ('ONBOARDING.APPLICATION.CREATE'),
    ('ONBOARDING.APPLICATION.EDIT'),
    ('MYBOOKINGS.BOOKINGS.VIEW'),
    ('MYBOOKINGS.BOOKINGS.CREATE'),
    ('MYBOOKINGS.BOOKINGS.EDIT'),
    ('MYSERVICES.APPOINTMENTS.VIEW'),
    ('MYSERVICES.APPOINTMENTS.CREATE'),
    ('MYSERVICES.APPOINTMENTS.EDIT')
) AS v(Code)
UNION ALL
SELECT 'PLATFORM_ADMIN', p.Code FROM @RbacPermissions p WHERE p.Scope = 'Platform'
UNION ALL
SELECT 'PLATFORM_STAFF', v.Code
FROM (VALUES
    ('ONBOARDING.REQUESTS.VIEW'),
    ('ONBOARDING.REQUESTS.EDIT'),
    -- Plan 04: staff may see the institutions and the accounts, never act in or on them.
    ('PLATFORM.INSTITUTIONS.VIEW'),
    ('PLATFORM.USERS.VIEW')
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

/*  Cloned into each institution when onboarding approves it (the onboarding plan): one per
    institution level and named for it, and LEADER at the staff level for a religious leader who
    signs in (Phase E, V1). In a care home LEADER holds nothing, because SERVICES is off there and
    the effective-permissions view drops a disabled module's permissions. System roles: the
    platform defines them, and the test fixture's reset leaves them alone.                      */
WITH GlobalRoles AS (
    SELECT InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, IsSystem, IsActive
    FROM auth.Roles
    WHERE InstitutionId IS NULL
)
MERGE GlobalRoles AS t
USING (
    SELECT l.Id AS RoleLevelId, v.Code, v.Name
    FROM (VALUES
        ('OWNER',   'OWNER',   N'Owner'),
        ('MANAGER', 'MANAGER', N'Manager'),
        ('STAFF',   'STAFF',   N'Staff'),
        ('LEADER',  'STAFF',   N'Religious leader')
    ) AS v(Code, LevelCode, Name)
    JOIN auth.RoleLevels l ON l.Code = v.LevelCode AND l.Scope = 'Institution'
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR t.RoleLevelId <> s.RoleLevelId OR t.IsSystem = 0 OR t.IsTemplate = 0)
    THEN UPDATE SET Name = s.Name, RoleLevelId = s.RoleLevelId, IsSystem = 1, IsTemplate = 1
WHEN NOT MATCHED THEN
    INSERT (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate, IsSystem, IsActive)
    VALUES (NULL, 'Institution', s.RoleLevelId, s.Code, s.Name, 1, 1, 1);

/*  What each template grants by default: the owner everything, the manager everything but the
    profile, staff the calendar, the slots, the services and the profile to read, and a leader only
    their own schedule. A leader's schedule is nobody else's, so the owner and manager templates
    leave it out; an owner who is also a leader is given LEADER too. Each must be inside its level's
    ceiling, or the view would drop it without a word - so that is checked, not trusted.       */
DECLARE @RbacTemplateGrants TABLE (RoleCode VARCHAR(50) NOT NULL, PermissionCode VARCHAR(150) NOT NULL);

INSERT @RbacTemplateGrants (RoleCode, PermissionCode)
SELECT 'OWNER', p.Code FROM @RbacPermissions p WHERE p.Scope = 'Institution' AND p.ScreenCode <> 'SERVICES.MYSCHEDULE'
UNION ALL
SELECT 'MANAGER', p.Code FROM @RbacPermissions p WHERE p.Scope = 'Institution' AND p.ScreenCode <> 'SERVICES.MYSCHEDULE' AND p.Code <> 'INSTITUTION.PROFILE.EDIT'
UNION ALL
SELECT 'STAFF', v.Code FROM (VALUES ('INSTITUTION.PROFILE.VIEW'), ('BOOKINGS.SLOTS.VIEW'), ('BOOKINGS.CALENDAR.VIEW'),
                                    ('SERVICES.TYPES.VIEW'), ('SERVICES.LEADERS.VIEW'), ('SERVICES.APPOINTMENTS.VIEW')) AS v(Code)
UNION ALL
SELECT 'LEADER', v.Code FROM (VALUES ('SERVICES.MYSCHEDULE.VIEW'), ('SERVICES.MYSCHEDULE.EDIT')) AS v(Code);

IF EXISTS (SELECT 1 FROM @RbacTemplateGrants g
           WHERE NOT EXISTS (SELECT 1 FROM auth.Roles r
                             JOIN auth.RoleLevels l ON l.Id = r.RoleLevelId
                             JOIN @RbacCeilings c   ON c.LevelCode = l.Code AND c.PermissionCode = g.PermissionCode
                             WHERE r.Code = g.RoleCode AND r.IsTemplate = 1 AND r.InstitutionId IS NULL))
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

/* ---------------------------------------------------------------- default modules per institution type */

/*  Modules that are not core but on by default for a type (Phase E, V12): religious services for
    the religious institution types, by Category, so a new religious type gets it without an edit
    here. Synced exactly, like the grants.                                                      */
MERGE org.InstitutionTypeDefaultModules AS t
USING (
    SELECT it.Id AS InstitutionTypeId, m.Id AS ModuleId
    FROM org.InstitutionTypes it
    JOIN auth.Modules m ON m.Code = 'SERVICES'
    WHERE it.Category = 'Religious'
) AS s
ON t.InstitutionTypeId = s.InstitutionTypeId AND t.ModuleId = s.ModuleId
WHEN NOT MATCHED BY TARGET THEN INSERT (InstitutionTypeId, ModuleId) VALUES (s.InstitutionTypeId, s.ModuleId)
WHEN NOT MATCHED BY SOURCE THEN DELETE;

/* ---------------------------------------------------------------- system role grants */

/*  What the global system roles hold (08_SystemRoles.sql): the donor what every signed-up person
    may do for themselves, the platform administrator every platform permission, the reviewer the
    review without the decision (plan 02, B1). Synced exactly, like the template grants, and each
    checked against its level's ceiling for the same reason.                                    */
DECLARE @RbacSystemGrants TABLE (RoleCode VARCHAR(50) NOT NULL, PermissionCode VARCHAR(150) NOT NULL);

INSERT @RbacSystemGrants (RoleCode, PermissionCode)
SELECT 'DONOR', v.Code
FROM (VALUES
    ('ONBOARDING.APPLICATION.VIEW'),
    ('ONBOARDING.APPLICATION.CREATE'),
    ('ONBOARDING.APPLICATION.EDIT'),
    ('MYBOOKINGS.BOOKINGS.VIEW'),
    ('MYBOOKINGS.BOOKINGS.CREATE'),
    ('MYBOOKINGS.BOOKINGS.EDIT'),
    ('MYSERVICES.APPOINTMENTS.VIEW'),
    ('MYSERVICES.APPOINTMENTS.CREATE'),
    ('MYSERVICES.APPOINTMENTS.EDIT')
) AS v(Code)
UNION ALL
SELECT 'PLATFORM_ADMIN', p.Code FROM @RbacPermissions p WHERE p.Scope = 'Platform'
UNION ALL
SELECT 'PLATFORM_REVIEWER', v.Code
FROM (VALUES
    ('ONBOARDING.REQUESTS.VIEW'),
    ('ONBOARDING.REQUESTS.EDIT'),
    ('PLATFORM.INSTITUTIONS.VIEW'),
    ('PLATFORM.USERS.VIEW')
) AS v(Code);

IF EXISTS (SELECT 1 FROM @RbacSystemGrants g
           WHERE NOT EXISTS (SELECT 1 FROM auth.Roles r
                             JOIN auth.RoleLevels l       ON l.Id = r.RoleLevelId
                             JOIN @RbacCeilings c         ON c.LevelCode = l.Code AND c.PermissionCode = g.PermissionCode
                             WHERE r.Code = g.RoleCode AND r.InstitutionId IS NULL AND r.IsSystem = 1 AND r.IsTemplate = 0))
    THROW 51006, '10_RbacCatalogue: a system role grant names an unknown role or is outside its level''s ceiling.', 1;

WITH SystemGrants AS (
    SELECT rp.RoleId, rp.PermissionId
    FROM auth.RolePermissions rp
    WHERE rp.RoleId IN (SELECT r.Id FROM auth.Roles r
                        WHERE r.IsSystem = 1 AND r.IsTemplate = 0 AND r.InstitutionId IS NULL)
)
MERGE SystemGrants AS t
USING (
    SELECT r.Id AS RoleId, p.Id AS PermissionId
    FROM @RbacSystemGrants g
    JOIN auth.Roles r       ON r.Code = g.RoleCode AND r.IsSystem = 1 AND r.IsTemplate = 0 AND r.InstitutionId IS NULL
    JOIN auth.Permissions p ON p.Code = g.PermissionCode
) AS s
ON t.RoleId = s.RoleId AND t.PermissionId = s.PermissionId
WHEN NOT MATCHED BY TARGET THEN INSERT (RoleId, PermissionId) VALUES (s.RoleId, s.PermissionId)
WHEN NOT MATCHED BY SOURCE THEN DELETE;
