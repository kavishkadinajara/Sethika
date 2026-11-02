CREATE TABLE booking.Bookings (
    Id                  BIGINT        IDENTITY(1,1) PRIMARY KEY,
    BookingNo           VARCHAR(20)   NOT NULL UNIQUE,
    InstitutionId       INT           NOT NULL REFERENCES org.Institutions(Id),
    SlotInstanceId      BIGINT        NOT NULL,
    DonorUserId         UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    InstitutionDonorId  INT           NULL,
    DonorName           NVARCHAR(150) NOT NULL,          -- snapshot for receipts
    DonorPhone          VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NOT NULL,
    SponsorDisplayName  NVARCHAR(150) NULL,
    IsAnonymous         BIT           NOT NULL DEFAULT 0,
    Headcount           SMALLINT      NOT NULL
        CONSTRAINT CK_Bookings_Headcount CHECK (Headcount > 0),
    ActualHeadcount     SMALLINT      NULL,
    FulfilmentMode      VARCHAR(20)   NOT NULL DEFAULT 'DonorBrings'
        CONSTRAINT CK_Bookings_FulfilmentMode CHECK (FulfilmentMode = 'DonorBrings' OR FulfilmentMode = 'InstitutionPrepares' OR FulfilmentMode = 'Caterer'),
    OccasionLookupId    INT           NULL REFERENCES core.LookupValues(Id),
    InMemoryOf          NVARCHAR(200) NULL,
    MemorialPlanId      INT           NULL REFERENCES booking.MemorialPlans(Id),
    MemorialRuleId      SMALLINT      NULL REFERENCES core.MemorialRules(Id),
    RecurringBookingId  INT           NULL REFERENCES booking.RecurringBookings(Id),
    Notes               NVARCHAR(500) NULL,
    Source              VARCHAR(20)   NOT NULL DEFAULT 'Online'
        CONSTRAINT CK_Bookings_Source CHECK (Source = 'Online' OR Source = 'Staff' OR Source = 'Recurring' OR Source = 'Waitlist' OR Source = 'Memorial' OR Source = 'Standing'),
    Status              VARCHAR(20)   NOT NULL DEFAULT 'Requested'
        CONSTRAINT CK_Bookings_Status CHECK (Status = 'Requested' OR Status = 'Approved' OR Status = 'Rejected' OR Status = 'Cancelled' OR Status = 'Completed' OR Status = 'NoShow'),
    CreatedBy           UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CreatedAt           DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    DecidedBy           UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    DecidedAt           DATETIME2(3)  NULL,
    RejectReason        NVARCHAR(300) NULL,
    CancelledBy         UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    CancelledAt         DATETIME2(3)  NULL,
    CancelReason        NVARCHAR(300) NULL,
    CompletedAt         DATETIME2(3)  NULL,
    RowVersion          ROWVERSION,
    -- Added by plan 08 (K3) at the end of the table, so the deploy is an ALTER: a day of a standing
    -- sponsorship. Its FK names the sponsorship's own donor, so the day is booked for that person.
    StandingSponsorshipId INT         NULL,
    CONSTRAINT FK_Bookings_SlotTenant  FOREIGN KEY (SlotInstanceId, InstitutionId)     REFERENCES booking.SlotInstances(Id, InstitutionId),
    CONSTRAINT FK_Bookings_DonorTenant FOREIGN KEY (InstitutionDonorId, InstitutionId) REFERENCES org.InstitutionDonors(Id, InstitutionId),
    CONSTRAINT UQ_Bookings_Tenant UNIQUE (Id, InstitutionId),
    CONSTRAINT CK_Bookings_Cancel CHECK (Status <> 'Cancelled' OR CancelledAt IS NOT NULL),
    CONSTRAINT CK_Bookings_Reject CHECK (Status <> 'Rejected'  OR RejectReason IS NOT NULL),
    CONSTRAINT CK_Bookings_Actual CHECK (ActualHeadcount IS NULL OR ActualHeadcount >= 0),
    CONSTRAINT FK_Bookings_StandingTenant FOREIGN KEY (StandingSponsorshipId, InstitutionId, InstitutionDonorId)
        REFERENCES booking.StandingSponsorships(Id, InstitutionId, InstitutionDonorId),
    -- Only a standing day names a sponsorship, and it always names the institution's donor too.
    CONSTRAINT CK_Bookings_Standing CHECK (
        (Source = 'Standing' AND StandingSponsorshipId IS NOT NULL AND InstitutionDonorId IS NOT NULL)
     OR (Source <> 'Standing' AND StandingSponsorshipId IS NULL))
);
GO

/* the same donor cannot hold two active bookings on the same slot-day */
CREATE UNIQUE INDEX UX_Bookings_DonorPerSlot ON booking.Bookings(SlotInstanceId, DonorPhone) WHERE Status IN ('Requested','Approved');
GO

CREATE INDEX IX_Bookings_Institution ON booking.Bookings(InstitutionId, Status, CreatedAt DESC);
GO

CREATE INDEX IX_Bookings_Donor       ON booking.Bookings(DonorUserId, CreatedAt DESC) WHERE DonorUserId IS NOT NULL;
GO

/* a sponsorship's days: the nightly job, letting a day go, the donor book */
CREATE INDEX IX_Bookings_Standing ON booking.Bookings(StandingSponsorshipId) INCLUDE (SlotInstanceId, Status) WHERE StandingSponsorshipId IS NOT NULL;
GO
