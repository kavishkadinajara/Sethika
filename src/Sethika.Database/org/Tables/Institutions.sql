CREATE TABLE org.Institutions (
    Id                     INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionCode        VARCHAR(20)   NOT NULL UNIQUE,
    Slug                   VARCHAR(150)  NOT NULL UNIQUE,
    RegistrationRequestId  INT           NOT NULL REFERENCES org.RegistrationRequests(Id),
    InstitutionTypeId      SMALLINT      NOT NULL REFERENCES org.InstitutionTypes(Id),
    Name                   NVARCHAR(200) NOT NULL,
    NameSi                 NVARCHAR(200) NULL,
    NameTa                 NVARCHAR(200) NULL,
    OfficialRegistrationNo VARCHAR(50)   NOT NULL,
    Description            NVARCHAR(2000) NULL,
    AddressLine1           NVARCHAR(200) NOT NULL,
    AddressLine2           NVARCHAR(200) NULL,
    City                   NVARCHAR(100) NOT NULL,
    DistrictId             SMALLINT      NOT NULL REFERENCES core.Districts(Id),
    DsDivisionId           SMALLINT      NULL REFERENCES core.DsDivisions(Id),
    Latitude               DECIMAL(9,6)  NOT NULL,
    Longitude              DECIMAL(9,6)  NOT NULL,
    Location               AS geography::Point(Latitude, Longitude, 4326) PERSISTED,  -- DB-side radius search; Redis GEO is the fast path
    Phone                  VARCHAR(20)   NOT NULL,
    Email                  NVARCHAR(256) NULL,
    ResidentCapacity       SMALLINT      NULL,
    DietaryNotes           NVARCHAR(500) NULL,     -- "vegetarian only", "halal only", "no spicy food for elders"
    LogoFileId             BIGINT        NULL REFERENCES core.Files(Id),
    CoverFileId            BIGINT        NULL REFERENCES core.Files(Id),
    Status                 VARCHAR(20)   NOT NULL DEFAULT 'Onboarding'
        CONSTRAINT CK_Institutions_Status CHECK (Status = 'Onboarding' OR Status = 'Active' OR Status = 'Suspended' OR Status = 'Deactivated'),
    IsPubliclyListed       BIT           NOT NULL DEFAULT 0,
    VerifiedBadgeLevel     TINYINT       NOT NULL DEFAULT 1
        CONSTRAINT CK_Institutions_VerifiedBadgeLevel CHECK (VerifiedBadgeLevel >= 0 AND VerifiedBadgeLevel <= 3),  -- 3 = documents + site visit
    NextReviewDue          DATE          NULL,     -- periodic re-verification
    PermissionsVersion     INT           NOT NULL DEFAULT 1,
    ApprovedBy             UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ApprovedAt             DATETIME2(3)  NOT NULL,
    CreatedAt              DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt              DATETIME2(3)  NULL,
    UpdatedBy              UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    IsDeleted              BIT           NOT NULL DEFAULT 0,
    RowVersion             ROWVERSION,
    CONSTRAINT UQ_Institutions_Request UNIQUE (RegistrationRequestId)
);
GO

CREATE INDEX IX_Institutions_District_Type ON org.Institutions(DistrictId, InstitutionTypeId) WHERE Status = 'Active' AND IsPubliclyListed = 1;
GO

CREATE SPATIAL INDEX SIX_Institutions_Location ON org.Institutions(Location);
GO
