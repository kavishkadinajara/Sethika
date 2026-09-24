CREATE TABLE giving.Donations (
    Id                    BIGINT        IDENTITY(1,1) PRIMARY KEY,
    ReceiptNo             VARCHAR(30)   NOT NULL UNIQUE,
    InstitutionId         INT           NOT NULL REFERENCES org.Institutions(Id),
    DonorUserId           UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    InstitutionDonorId    INT           NULL,
    DonorName             NVARCHAR(150) NOT NULL,
    DonorPhone            VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NULL,
    IsAnonymous           BIT           NOT NULL DEFAULT 0,
    Kind                  VARCHAR(10)   NOT NULL
        CONSTRAINT CK_Donations_Kind CHECK (Kind = 'Money' OR Kind = 'InKind'),
    Amount                DECIMAL(12,2) NULL,
    CurrencyCode          CHAR(3)       NOT NULL DEFAULT 'LKR',
    PaymentMethodLookupId INT           NULL REFERENCES core.LookupValues(Id),
    ItemDescription       NVARCHAR(500) NULL,
    EstimatedValue        DECIMAL(12,2) NULL,
    NeedId                INT           NULL,
    PledgeId              INT           NULL,
    BookingId             BIGINT        NULL,
    ReceiptFileId         BIGINT        NULL REFERENCES core.Files(Id),
    DonatedAt             DATETIME2(3)  NOT NULL,
    RecordedBy            UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    RecordedAt            DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    IsVoided              BIT           NOT NULL DEFAULT 0,
    VoidedBy              UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    VoidReason            NVARCHAR(300) NULL,
    CONSTRAINT FK_Donations_NeedTenant    FOREIGN KEY (NeedId, InstitutionId)             REFERENCES giving.Needs(Id, InstitutionId),
    CONSTRAINT FK_Donations_PledgeTenant  FOREIGN KEY (PledgeId, InstitutionId)           REFERENCES giving.Pledges(Id, InstitutionId),
    CONSTRAINT FK_Donations_BookingTenant FOREIGN KEY (BookingId, InstitutionId)          REFERENCES booking.Bookings(Id, InstitutionId),
    CONSTRAINT FK_Donations_DonorTenant   FOREIGN KEY (InstitutionDonorId, InstitutionId) REFERENCES org.InstitutionDonors(Id, InstitutionId),
    CONSTRAINT CK_Donations_Money  CHECK (Kind <> 'Money'  OR (Amount IS NOT NULL AND Amount > 0)),
    CONSTRAINT CK_Donations_InKind CHECK (Kind <> 'InKind' OR ItemDescription IS NOT NULL),
    CONSTRAINT CK_Donations_Void   CHECK (IsVoided = 0 OR (VoidedBy IS NOT NULL AND VoidReason IS NOT NULL)),
    CONSTRAINT CK_Donations_VoidFourEyes CHECK (VoidedBy IS NULL OR VoidedBy <> RecordedBy)   -- maker-checker
);
GO

CREATE INDEX IX_Donations_Institution_Date ON giving.Donations(InstitutionId, DonatedAt DESC) WHERE IsVoided = 0;
GO
