/*  Role levels (plan A15). Rank orders seniority within a scope, 1 the most senior; a role may
    only be managed by someone of strictly more senior rank. The platform defines these and
    institutions create their own roles at them. Each level's ceiling (auth.RoleLevelPermissions)
    arrives with the RBAC catalogue.

    A Scope change on a level that roles already use fails on FK_Roles_Level, deliberately: moving
    a level between scopes is a migration to plan, not a seed edit.                        */
MERGE auth.RoleLevels AS t
USING (VALUES
 (1, 'Platform',    1, 'PLATFORM_ADMIN', N'Platform administrator'),
 (2, 'Platform',    2, 'PLATFORM_STAFF', N'Platform staff'),
 (3, 'Institution', 1, 'OWNER',          N'Owner'),
 (4, 'Institution', 2, 'MANAGER',        N'Manager'),
 (5, 'Institution', 3, 'STAFF',          N'Staff'),
 (6, 'Donor',       1, 'DONOR',          N'Donor')) AS s(Id, Scope, Rank, Code, Name)
ON t.Id = s.Id
WHEN MATCHED AND (t.Scope <> s.Scope OR t.Rank <> s.Rank OR t.Code <> s.Code OR t.Name <> s.Name)
    THEN UPDATE SET Scope = s.Scope, Rank = s.Rank, Code = s.Code, Name = s.Name
WHEN NOT MATCHED THEN INSERT (Id, Scope, Rank, Code, Name) VALUES (s.Id, s.Scope, s.Rank, s.Code, s.Name);
