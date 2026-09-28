/*  Fixture for the persistence and permission tests.

    Two tenants, each with one booking, so a query as tenant A can be checked for tenant B's
    row. Plus one institution role over the seeded BOOKINGS permissions. The RBAC catalogue,
    the ceilings and the template roles are reference data from the dacpac
    (Scripts/Seed/10_RbacCatalogue.sql), so the fixture uses them and never deletes them.

    Runs on a connection stamped IsPlatform = 1, because the RLS BLOCK predicates would
    otherwise refuse the cross-tenant inserts.

    Deterministic and re-runnable: fixed GUIDs, and everything is cleared first. Safe only
    against the dedicated Sethika_Test database.                                          */

ALTER TABLE auth.Roles                    SET (SYSTEM_VERSIONING = OFF);
ALTER TABLE auth.UserRoles                SET (SYSTEM_VERSIONING = OFF);
ALTER TABLE auth.RolePermissions          SET (SYSTEM_VERSIONING = OFF);
ALTER TABLE auth.UserPermissionOverrides  SET (SYSTEM_VERSIONING = OFF);
ALTER TABLE org.InstitutionModules        SET (SYSTEM_VERSIONING = OFF);
GO

DECLARE @alphaUser  UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @betaUser   UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @typeId     SMALLINT = (SELECT Id FROM org.InstitutionTypes WHERE Code = 'TEMPLE');
DECLARE @districtId SMALLINT = (SELECT Id FROM core.Districts       WHERE Code = 'CMB');

/*  System versioning is suspended for the reset.

    SysStartTime is stamped from the transaction's begin time, so deleting a row that was
    inserted moments earlier can fail with "transaction time was earlier than period start
    time" when two reseeds land in the same clock tick. xunit builds a fresh test class per
    test method, so the fixture reseeds once per test and hits exactly that. Suspending
    versioning removes the timing dependency instead of hoping the clock cooperates, and
    lets the history tables be cleared too - otherwise fixture churn accumulates there
    forever.                                                                              */

/*  Reset, child-before-parent. Every FK direction matters here: get the order wrong and a
    DELETE fails, later INSERTs collide on duplicate keys, and the real cause is six errors
    upstream of where it surfaces.

    Sign-in history and refresh tokens go first: sign-in tests write both against the
    fixture's users and institutions, and a test that dies before its own cleanup would
    otherwise leave rows that block every later reset.                                    */
DELETE FROM auth.RefreshTokens;
DELETE FROM auth.LoginHistory;
DELETE FROM auth.UserRoles;
DELETE FROM auth.UserRolesHistory;
DELETE rp FROM auth.RolePermissions rp
JOIN auth.Roles r ON r.Id = rp.RoleId
WHERE r.IsSystem = 0;                        -- template grants are reference data from the dacpac
DELETE FROM auth.RolePermissionsHistory;
DELETE FROM auth.UserPermissionOverrides;
DELETE FROM auth.UserPermissionOverridesHistory;
DELETE FROM auth.Roles WHERE IsSystem = 0;   -- system roles (DONOR) are reference data from the dacpac
DELETE FROM auth.RolesHistory;
DELETE FROM org.InstitutionModules;
DELETE FROM org.InstitutionModulesHistory;
DELETE FROM booking.Bookings;
DELETE FROM booking.SlotInstances;
DELETE FROM booking.MealSlots;
DELETE FROM org.Institutions;
DELETE FROM org.RegistrationRequests;
DELETE FROM auth.Users WHERE Id IN (@alphaUser, @betaUser);


INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PreferredLanguage, Status, SecurityStamp)
VALUES (@alphaUser, 'InstitutionStaff', N'Alpha Steward', '0771000001', 'en', 'Active', 'stamp-alpha'),
       (@betaUser,  'InstitutionStaff', N'Beta Steward',  '0772000002', 'en', 'Active', 'stamp-beta');

/*  Left at 'Draft' deliberately. Approving a request requires CreatedInstitutionId, which
    requires the institution, which requires the request - and 'Submitted' would additionally
    demand a signed declaration document. None of that changes what RLS or the permission
    view do, so the fixture stops at the status that satisfies every CHECK on its own.     */
INSERT org.RegistrationRequests
    (RequestNo, ApplicantUserId, InstitutionTypeId, ProposedName, OfficialRegistrationNo,
     RegisteringAuthority, RegistrationDate, AddressLine1, City, DistrictId, Latitude, Longitude,
     OfficePhone, ApplicantFullName, ApplicantNic, ApplicantDesignation, ApplicantPhone, Status)
VALUES ('REQ-ALPHA', @alphaUser, @typeId, N'Alpha Temple', 'REG-ALPHA', N'Authority', '2024-01-01',
        N'1 Alpha Road', N'Colombo', @districtId, 6.927100, 79.861200,
        '0111000001', N'Alpha Steward', '901234567V', N'Trustee', '0771000001', 'Draft'),
       ('REQ-BETA',  @betaUser,  @typeId, N'Beta Temple',  'REG-BETA',  N'Authority', '2024-01-01',
        N'2 Beta Road',  N'Colombo', @districtId, 6.928100, 79.862200,
        '0111000002', N'Beta Steward',  '901234568V', N'Trustee', '0772000002', 'Draft');

INSERT org.Institutions
    (InstitutionCode, Slug, RegistrationRequestId, InstitutionTypeId, Name, OfficialRegistrationNo,
     AddressLine1, City, DistrictId, Latitude, Longitude, Phone, Status, ApprovedBy, ApprovedAt)
SELECT 'ALPHA', 'alpha-temple', r.Id, @typeId, N'Alpha Temple', 'REG-ALPHA',
       N'1 Alpha Road', N'Colombo', @districtId, 6.927100, 79.861200, '0111000001',
       'Active', @alphaUser, SYSUTCDATETIME()
FROM org.RegistrationRequests r WHERE r.RequestNo = 'REQ-ALPHA'
UNION ALL
SELECT 'BETA', 'beta-temple', r.Id, @typeId, N'Beta Temple', 'REG-BETA',
       N'2 Beta Road', N'Colombo', @districtId, 6.928100, 79.862200, '0111000002',
       'Active', @betaUser, SYSUTCDATETIME()
FROM org.RegistrationRequests r WHERE r.RequestNo = 'REQ-BETA';

DECLARE @alphaId INT = (SELECT Id FROM org.Institutions WHERE InstitutionCode = 'ALPHA');
DECLARE @betaId  INT = (SELECT Id FROM org.Institutions WHERE InstitutionCode = 'BETA');

INSERT booking.MealSlots (InstitutionId, Name, StartTime, EndTime, MaxHeadcount, CreatedBy)
VALUES (@alphaId, N'Alpha Dana', '11:00', '12:00', 50, @alphaUser),
       (@betaId,  N'Beta Dana',  '11:00', '12:00', 50, @betaUser);

INSERT booking.SlotInstances (InstitutionId, MealSlotId, SlotDate, MaxSponsors, MaxHeadcount)
SELECT s.InstitutionId, s.Id, '2026-01-01', s.MaxSponsors, s.MaxHeadcount
FROM booking.MealSlots s;

INSERT booking.Bookings
    (BookingNo, InstitutionId, SlotInstanceId, DonorName, DonorPhone, Headcount, CreatedBy)
SELECT CASE WHEN si.InstitutionId = @alphaId THEN 'BK-ALPHA' ELSE 'BK-BETA' END,
       si.InstitutionId, si.Id,
       CASE WHEN si.InstitutionId = @alphaId THEN N'Alpha Donor' ELSE N'Beta Donor' END,
       CASE WHEN si.InstitutionId = @alphaId THEN '0771111111' ELSE '0772222222' END,
       10,
       CASE WHEN si.InstitutionId = @alphaId THEN @alphaUser ELSE @betaUser END
FROM booking.SlotInstances si;

/* ---------------------------- RBAC ---------------------------- */

/*  The seeded BOOKINGS permissions: VIEW, and APPROVE, which the catalogue marks RequiresMfa so
    the step-up path has something real to read. The steward sits at STAFF, whose ceiling holds
    both - a role outside its level's ceiling would grant nothing (plan A15).               */
DECLARE @moduleId SMALLINT = (SELECT Id FROM auth.Modules WHERE Code = 'BOOKINGS');

INSERT auth.Roles (InstitutionId, Scope, RoleLevelId, Code, Name, IsTemplate)
VALUES (@alphaId, 'Institution', (SELECT Id FROM auth.RoleLevels WHERE Code = 'STAFF'),
        'ALPHA_STEWARD', N'Alpha Steward', 0);
DECLARE @roleId INT = CAST(SCOPE_IDENTITY() AS INT);

INSERT auth.RolePermissions (RoleId, PermissionId)
SELECT @roleId, p.Id FROM auth.Permissions p
WHERE p.Code IN ('BOOKINGS.CALENDAR.VIEW', 'BOOKINGS.CALENDAR.APPROVE');

INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
VALUES (@alphaUser, @roleId, @alphaId, 1);

/*  The module must be enabled for the institution or the view yields nothing: effective
    permissions are role grants INTERSECTED with the institution's enabled modules.       */
INSERT org.InstitutionModules (InstitutionId, ModuleId, IsEnabled, Source, ChangedBy)
VALUES (@alphaId, @moduleId, 1, 'Manual', @alphaUser);
GO

ALTER TABLE auth.Roles SET
    (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.RolesHistory, DATA_CONSISTENCY_CHECK = ON));
ALTER TABLE auth.UserRoles SET
    (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.UserRolesHistory, DATA_CONSISTENCY_CHECK = ON));
ALTER TABLE auth.RolePermissions SET
    (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.RolePermissionsHistory, DATA_CONSISTENCY_CHECK = ON));
ALTER TABLE auth.UserPermissionOverrides SET
    (SYSTEM_VERSIONING = ON (HISTORY_TABLE = auth.UserPermissionOverridesHistory, DATA_CONSISTENCY_CHECK = ON));
ALTER TABLE org.InstitutionModules SET
    (SYSTEM_VERSIONING = ON (HISTORY_TABLE = org.InstitutionModulesHistory, DATA_CONSISTENCY_CHECK = ON));
