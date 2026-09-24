CREATE TABLE org.RegistrationRequests (
    Id                     INT           IDENTITY(1,1) PRIMARY KEY,
    RequestNo              VARCHAR(20)   NOT NULL UNIQUE,
    ApplicantUserId        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InstitutionTypeId      SMALLINT      NOT NULL REFERENCES org.InstitutionTypes(Id),
    ProposedName           NVARCHAR(200) NOT NULL,
    NameSi                 NVARCHAR(200) NULL,
    NameTa                 NVARCHAR(200) NULL,
    OfficialRegistrationNo VARCHAR(50)   NOT NULL,
    RegisteringAuthority   NVARCHAR(200) NOT NULL,
    RegistrationDate       DATE          NOT NULL,
    EstablishedYear        SMALLINT      NULL
        CONSTRAINT CK_RegReq_EstablishedYear CHECK (EstablishedYear >= 1 AND EstablishedYear <= 2100),
    ResidentCapacity       SMALLINT      NULL
        CONSTRAINT CK_RegReq_ResidentCapacity CHECK (ResidentCapacity > 0),
    Description            NVARCHAR(2000) NULL,
    AddressLine1           NVARCHAR(200) NOT NULL,
    AddressLine2           NVARCHAR(200) NULL,
    City                   NVARCHAR(100) NOT NULL,
    DistrictId             SMALLINT      NOT NULL REFERENCES core.Districts(Id),
    DsDivisionId           SMALLINT      NULL REFERENCES core.DsDivisions(Id),
    GnDivision             NVARCHAR(100) NULL,
    PostalCode             VARCHAR(10)   NULL,
    Latitude               DECIMAL(9,6)  NOT NULL
        CONSTRAINT CK_RegReq_Latitude CHECK (Latitude >= 5.5 AND Latitude <= 10.0),
    Longitude              DECIMAL(9,6)  NOT NULL
        CONSTRAINT CK_RegReq_Longitude CHECK (Longitude >= 79.3 AND Longitude <= 82.1),
    OfficePhone            VARCHAR(20)   NOT NULL,
    OfficeEmail            NVARCHAR(256) NULL,
    Website                NVARCHAR(200) NULL,
    ApplicantFullName      NVARCHAR(150) NOT NULL,
    ApplicantNic           VARCHAR(12)   MASKED WITH (FUNCTION = 'partial(2, "XXXXXXX", 2)') NOT NULL,
    ApplicantDesignation   NVARCHAR(100) NOT NULL,
    ApplicantPhone         VARCHAR(20)   NOT NULL,
    ApplicantEmail         NVARCHAR(256) NULL,
    DeclarationDocumentId  SMALLINT      NULL REFERENCES core.LegalDocuments(Id),   -- exact declaration text accepted
    DeclarationAcceptedAt  DATETIME2(3)  NULL,
    DeclarationIp          VARCHAR(45)   NULL,
    RequestedPlanId        SMALLINT      NULL REFERENCES org.Plans(Id),
    RiskScore              TINYINT       NULL
        CONSTRAINT CK_RegReq_RiskScore CHECK (RiskScore >= 0 AND RiskScore <= 100),   -- auto checks: duplicate docs, reused NIC, location clash
    RiskFlags              NVARCHAR(500) NULL,
    Status                 VARCHAR(20)   NOT NULL DEFAULT 'Draft'
        CONSTRAINT CK_RegReq_Status CHECK (Status = 'Draft' OR Status = 'Submitted' OR Status = 'UnderReview' OR Status = 'InfoRequested' OR Status = 'Approved' OR Status = 'Rejected' OR Status = 'Withdrawn'),
    SubmittedAt            DATETIME2(3)  NULL,
    SlaDueAt               DATETIME2(3)  NULL,   -- e.g. decide within 5 working days
    AssignedReviewerId     UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    DecidedBy              UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    DecidedAt              DATETIME2(3)  NULL,
    RejectReasonLookupId   INT           NULL REFERENCES core.LookupValues(Id),
    DecisionNote           NVARCHAR(1000) NULL,
    CreatedInstitutionId   INT           NULL,
    CreatedAt              DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt              DATETIME2(3)  NULL,
    RowVersion             ROWVERSION,
    CONSTRAINT CK_RegReq_DeclarationOnSubmit
        CHECK (Status = 'Draft' OR Status = 'Withdrawn' OR (DeclarationDocumentId IS NOT NULL AND DeclarationAcceptedAt IS NOT NULL)),
    CONSTRAINT CK_RegReq_DecisionFields
        CHECK (NOT (Status = 'Approved' OR Status = 'Rejected') OR (DecidedBy IS NOT NULL AND DecidedAt IS NOT NULL)),
    CONSTRAINT CK_RegReq_ApprovedHasInstitution CHECK (Status <> 'Approved' OR CreatedInstitutionId IS NOT NULL),
    CONSTRAINT CK_RegReq_RejectHasReason        CHECK (Status <> 'Rejected' OR RejectReasonLookupId IS NOT NULL),
    CONSTRAINT CK_RegReq_ReviewerNotApplicant   CHECK (AssignedReviewerId IS NULL OR AssignedReviewerId <> ApplicantUserId),
    CONSTRAINT CK_RegReq_DeciderNotApplicant    CHECK (DecidedBy IS NULL OR DecidedBy <> ApplicantUserId),
    CONSTRAINT FK_RegReq_Institution FOREIGN KEY (CreatedInstitutionId) REFERENCES org.Institutions(Id)
);
GO

CREATE UNIQUE INDEX UX_RegReq_ActiveRegNo
    ON org.RegistrationRequests(InstitutionTypeId, OfficialRegistrationNo)
    WHERE Status IN ('Submitted','UnderReview','InfoRequested','Approved');
GO

CREATE INDEX IX_RegReq_Status   ON org.RegistrationRequests(Status, SlaDueAt);
GO

CREATE INDEX IX_RegReq_Reviewer ON org.RegistrationRequests(AssignedReviewerId, Status);
GO
