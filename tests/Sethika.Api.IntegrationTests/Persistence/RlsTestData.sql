/*  Two tenants, each with one booking, so a query as tenant A can be checked for
    tenant B's row. Runs on a connection stamped IsPlatform = 1, because the RLS BLOCK
    predicates would otherwise refuse the cross-tenant inserts.

    Deterministic and re-runnable: fixed GUIDs, and everything is cleared first. Safe
    only against the dedicated Sethika_Test database.                                  */

DECLARE @alphaUser  UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @betaUser   UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @typeId     SMALLINT = (SELECT Id FROM org.InstitutionTypes WHERE Code = 'TEMPLE');
DECLARE @districtId SMALLINT = (SELECT Id FROM core.Districts       WHERE Code = 'CMB');

DELETE FROM booking.Bookings;
DELETE FROM booking.SlotInstances;
DELETE FROM booking.MealSlots;
DELETE FROM org.Institutions;
DELETE FROM org.RegistrationRequests;
DELETE FROM auth.Users WHERE Id IN (@alphaUser, @betaUser);

INSERT auth.Users (Id, UserType, FullName, PhoneNumber, PreferredLanguage, Status, SecurityStamp)
VALUES (@alphaUser, 'InstitutionStaff', N'Alpha Steward', '0771000001', 'en', 'Active', 'stamp-alpha'),
       (@betaUser,  'InstitutionStaff', N'Beta Steward',  '0772000002', 'en', 'Active', 'stamp-beta');

INSERT org.RegistrationRequests
    (RequestNo, ApplicantUserId, InstitutionTypeId, ProposedName, OfficialRegistrationNo,
     RegisteringAuthority, RegistrationDate, AddressLine1, City, DistrictId, Latitude, Longitude,
     OfficePhone, ApplicantFullName, ApplicantNic, ApplicantDesignation, ApplicantPhone, Status)
/*  Left at 'Draft' deliberately. Approving a request requires CreatedInstitutionId,
    which requires the institution, which requires the request - and 'Submitted' would
    additionally demand a signed declaration document. None of that changes what RLS
    does, so the fixture stops at the status that satisfies every CHECK on its own.   */
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
