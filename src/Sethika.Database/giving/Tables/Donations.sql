/*  A gift an institution received: money or items (plan 09, M1). Never edited and never deleted, only
    voided by a second person (CLAUDE.md's maker-checker, M6); trg_Donations_Immutable makes the
    database itself refuse anything else (M7).

    ReceiptNo comes from the institution's own RECEIPT series (M2), so it is unique inside an
    institution, not across the platform: two institutions both start at RCP-2026-000001. PublicId
    addresses a receipt in URLs and from the donor's side (CLAUDE.md: never Id).

    Who gave is a snapshot (M3), like a booking's: the donor book row's name and phone at the time, or
    a walk-in's typed name. DonorUserId is the account that may read the receipt (RLS fn_TenantOrOwner):
    a signed-in donor's, or the account a donor book row is linked to.                              */
CREATE TABLE giving.Donations (
    Id                    BIGINT        IDENTITY(1,1) PRIMARY KEY,
    ReceiptNo             VARCHAR(30)   NOT NULL,
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
    -- Added by plan 09 (D.1) at the end of the table, so the deploy is an ALTER, never a rebuild of a
    -- table under RLS. The table was empty everywhere when they were added, so NOT NULL needs no default.
    PublicId              UNIQUEIDENTIFIER NOT NULL,     -- v7, set in C#
    Quantity              DECIMAL(10,2) NULL,            -- how many items, for a need's progress (M15)
    VoidedAt              DATETIME2(3)  NULL,
    Note                  NVARCHAR(300) NULL,
    RowVersion            ROWVERSION,
    CONSTRAINT UQ_Donations_Receipt   UNIQUE (InstitutionId, ReceiptNo),
    CONSTRAINT UQ_Donations_PublicId  UNIQUE (PublicId),
    -- Target of giving.OnlinePayments' composite FK, so a paid order names a donation of its own institution.
    CONSTRAINT UQ_Donations_Tenant    UNIQUE (Id, InstitutionId),
    CONSTRAINT FK_Donations_NeedTenant    FOREIGN KEY (NeedId, InstitutionId)             REFERENCES giving.Needs(Id, InstitutionId),
    CONSTRAINT FK_Donations_PledgeTenant  FOREIGN KEY (PledgeId, InstitutionId)           REFERENCES giving.Pledges(Id, InstitutionId),
    CONSTRAINT FK_Donations_BookingTenant FOREIGN KEY (BookingId, InstitutionId)          REFERENCES booking.Bookings(Id, InstitutionId),
    CONSTRAINT FK_Donations_DonorTenant   FOREIGN KEY (InstitutionDonorId, InstitutionId) REFERENCES org.InstitutionDonors(Id, InstitutionId),
    CONSTRAINT CK_Donations_Money  CHECK (Kind <> 'Money'  OR (Amount IS NOT NULL AND Amount > 0 AND PaymentMethodLookupId IS NOT NULL)),
    CONSTRAINT CK_Donations_InKind CHECK (Kind <> 'InKind' OR (ItemDescription IS NOT NULL AND Amount IS NULL)),
    CONSTRAINT CK_Donations_Quantity CHECK (Quantity IS NULL OR (Kind = 'InKind' AND Quantity > 0)),
    CONSTRAINT CK_Donations_Estimate CHECK (EstimatedValue IS NULL OR (Kind = 'InKind' AND EstimatedValue > 0)),
    -- Voided means all three of who, why and when; not voided means none of them.
    CONSTRAINT CK_Donations_Void   CHECK ((IsVoided = 1 AND VoidedBy IS NOT NULL AND VoidReason IS NOT NULL AND VoidedAt IS NOT NULL)
                                       OR (IsVoided = 0 AND VoidedBy IS NULL AND VoidReason IS NULL AND VoidedAt IS NULL)),
    CONSTRAINT CK_Donations_VoidFourEyes CHECK (VoidedBy IS NULL OR VoidedBy <> RecordedBy)   -- maker-checker
);
GO

-- The institution's list and the month's report read voided gifts too (listed apart, M6), so unfiltered.
CREATE INDEX IX_Donations_Institution_Date ON giving.Donations(InstitutionId, DonatedAt DESC);
GO

-- My giving (M18): a donor's own receipts, newest first.
CREATE INDEX IX_Donations_Donor ON giving.Donations(DonorUserId, DonatedAt DESC) WHERE DonorUserId IS NOT NULL;
GO

-- A donor book row's gifts, for linking (K36) and its history.
CREATE INDEX IX_Donations_InstitutionDonor ON giving.Donations(InstitutionDonorId) WHERE InstitutionDonorId IS NOT NULL;
GO
