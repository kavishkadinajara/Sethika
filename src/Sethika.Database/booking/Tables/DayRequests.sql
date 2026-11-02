/* Asking for a day a standing sponsor holds (plan 08, K6). A donor asks; asking holds nothing. The
   institution gives it (the standing day is released, StandingExceptions, and the asker is booked,
   BookingId) or declines; the donor may withdraw while it is open. RequestNo is the request's
   address, from core.NumberSeries. */
CREATE TABLE booking.DayRequests (
    Id                    INT           IDENTITY(1,1) PRIMARY KEY,
    RequestNo             VARCHAR(20)   NOT NULL CONSTRAINT UQ_DayRequests_No UNIQUE,
    InstitutionId         INT           NOT NULL CONSTRAINT FK_DayRequests_Institution REFERENCES org.Institutions(Id),
    MealSlotId            INT           NOT NULL,
    RequestedDate         DATE          NOT NULL,
    DonorUserId           UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_DayRequests_Donor REFERENCES auth.Users(Id),
    Headcount             SMALLINT      NOT NULL
        CONSTRAINT CK_DayRequests_Headcount CHECK (Headcount > 0),
    Note                  NVARCHAR(300) NULL,
    Status                VARCHAR(10)   NOT NULL CONSTRAINT DF_DayRequests_Status DEFAULT 'Open'
        CONSTRAINT CK_DayRequests_Status CHECK (Status = 'Open' OR Status = 'Given' OR Status = 'Declined' OR Status = 'Withdrawn'),
    StandingSponsorshipId INT           NULL,             -- Given: whose day was let go
    BookingId             BIGINT        NULL,             -- Given: the asker's booking
    DecidedBy             UNIQUEIDENTIFIER NULL CONSTRAINT FK_DayRequests_DecidedBy REFERENCES auth.Users(Id),
    DecidedAt             DATETIME2(3)  NULL,
    DeclineReason         NVARCHAR(300) NULL,
    CreatedAt             DATETIME2(3)  NOT NULL CONSTRAINT DF_DayRequests_CreatedAt DEFAULT SYSUTCDATETIME(),
    RowVersion            ROWVERSION,
    CONSTRAINT FK_DayRequests_SlotTenant     FOREIGN KEY (MealSlotId, InstitutionId)            REFERENCES booking.MealSlots(Id, InstitutionId),
    CONSTRAINT FK_DayRequests_StandingTenant FOREIGN KEY (StandingSponsorshipId, InstitutionId) REFERENCES booking.StandingSponsorships(Id, InstitutionId),
    CONSTRAINT FK_DayRequests_BookingTenant  FOREIGN KEY (BookingId, InstitutionId)             REFERENCES booking.Bookings(Id, InstitutionId),
    CONSTRAINT UQ_DayRequests_Tenant UNIQUE (Id, InstitutionId),
    -- Open has no answer; Given names the day let go and the booking made; Declined and Withdrawn
    -- have only their who and when.
    CONSTRAINT CK_DayRequests_Decision CHECK (
        (Status = 'Open'  AND DecidedBy IS NULL     AND DecidedAt IS NULL     AND StandingSponsorshipId IS NULL     AND BookingId IS NULL)
     OR (Status = 'Given' AND DecidedBy IS NOT NULL AND DecidedAt IS NOT NULL AND StandingSponsorshipId IS NOT NULL AND BookingId IS NOT NULL)
     OR ((Status = 'Declined' OR Status = 'Withdrawn')
                          AND DecidedBy IS NOT NULL AND DecidedAt IS NOT NULL AND StandingSponsorshipId IS NULL     AND BookingId IS NULL)),
    CONSTRAINT CK_DayRequests_DeclineReason CHECK (DeclineReason IS NULL OR Status = 'Declined')
);
GO

/* one open request per donor and slot-day (K6) */
CREATE UNIQUE INDEX UX_DayRequests_OpenPerDonor ON booking.DayRequests(MealSlotId, RequestedDate, DonorUserId) WHERE Status = 'Open';
GO

/* the institution's tray, and "my day requests" */
CREATE INDEX IX_DayRequests_Institution ON booking.DayRequests(InstitutionId, Status, CreatedAt DESC);
GO
CREATE INDEX IX_DayRequests_Donor ON booking.DayRequests(DonorUserId, CreatedAt DESC);
GO
