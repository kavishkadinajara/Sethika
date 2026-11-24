/* A standing sponsorship (plan 08, K2): one of the institution's own donors gives a meal on a
   pattern the institution set up for them. Each day becomes an ordinary booking, Source = 'Standing'
   (K3), made by the engine and approved at once; a standing day holds the place (Q5).

   Patterns, each with its own fields and no others (CK_Standing_Shape):
     Once     the one day StartDate (EndDate = StartDate)
     Daily    every day
     Weekly   WeekDays, a mask: bit 0 Sunday ... bit 6 Saturday
     Monthly  MonthDays, a mask: bit 0 the 1st ... bit 30 the 31st (several days a month), every
              MonthInterval months counted from StartDate's month (2 every other month, 3 quarterly)
     Yearly   YearMonth and YearDay
   A day that does not exist in a month (the 31st in April, 29 February in a common year) is skipped,
   never moved (Q3). */
CREATE TABLE booking.StandingSponsorships (
    Id                 INT           IDENTITY(1,1) PRIMARY KEY,
    PublicId           UNIQUEIDENTIFIER NOT NULL,         -- v7, set in C#
    InstitutionId      INT           NOT NULL CONSTRAINT FK_Standing_Institution REFERENCES org.Institutions(Id),
    InstitutionDonorId INT           NOT NULL,
    MealSlotId         INT           NOT NULL,
    Pattern            VARCHAR(10)   NOT NULL
        CONSTRAINT CK_Standing_Pattern CHECK (Pattern = 'Once' OR Pattern = 'Daily' OR Pattern = 'Weekly' OR Pattern = 'Monthly' OR Pattern = 'Yearly'),
    MonthInterval      TINYINT       NOT NULL CONSTRAINT DF_Standing_MonthInterval DEFAULT 1
        CONSTRAINT CK_Standing_MonthInterval CHECK (MonthInterval >= 1 AND MonthInterval <= 12),
    WeekDays           TINYINT       NULL
        CONSTRAINT CK_Standing_WeekDays CHECK (WeekDays >= 1 AND WeekDays <= 127),
    MonthDays          INT           NULL                 -- 31 bits: every positive INT is a mask
        CONSTRAINT CK_Standing_MonthDays CHECK (MonthDays >= 1),
    YearMonth          TINYINT       NULL
        CONSTRAINT CK_Standing_YearMonth CHECK (YearMonth >= 1 AND YearMonth <= 12),
    YearDay            TINYINT       NULL
        CONSTRAINT CK_Standing_YearDay CHECK (YearDay >= 1 AND YearDay <= 31),
    StartDate          DATE          NOT NULL,
    EndDate            DATE          NULL,
    Headcount          SMALLINT      NOT NULL
        CONSTRAINT CK_Standing_Headcount CHECK (Headcount > 0),
    FulfilmentMode     VARCHAR(20)   NOT NULL CONSTRAINT DF_Standing_FulfilmentMode DEFAULT 'DonorBrings'
        CONSTRAINT CK_Standing_FulfilmentMode CHECK (FulfilmentMode = 'DonorBrings' OR FulfilmentMode = 'InstitutionPrepares' OR FulfilmentMode = 'Caterer'),
    InMemoryOf         NVARCHAR(200) NULL,
    Notes              NVARCHAR(500) NULL,
    Status             VARCHAR(10)   NOT NULL CONSTRAINT DF_Standing_Status DEFAULT 'Active'
        CONSTRAINT CK_Standing_Status CHECK (Status = 'Active' OR Status = 'Ended'),
    EndedAt            DATETIME2(3)  NULL,
    EndedBy            UNIQUEIDENTIFIER NULL CONSTRAINT FK_Standing_EndedBy REFERENCES auth.Users(Id),
    CreatedBy          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Standing_CreatedBy REFERENCES auth.Users(Id),
    CreatedAt          DATETIME2(3)  NOT NULL CONSTRAINT DF_Standing_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy          UNIQUEIDENTIFIER NULL CONSTRAINT FK_Standing_UpdatedBy REFERENCES auth.Users(Id),
    UpdatedAt          DATETIME2(3)  NULL,
    RowVersion         ROWVERSION,
    CONSTRAINT FK_Standing_DonorTenant FOREIGN KEY (InstitutionDonorId, InstitutionId) REFERENCES org.InstitutionDonors(Id, InstitutionId),
    CONSTRAINT FK_Standing_SlotTenant  FOREIGN KEY (MealSlotId, InstitutionId)         REFERENCES booking.MealSlots(Id, InstitutionId),
    CONSTRAINT UQ_Standing_Tenant UNIQUE (Id, InstitutionId),
    -- Target for a standing booking's FK, which so names the sponsorship's own donor (Bookings).
    CONSTRAINT UQ_Standing_TenantDonor UNIQUE (Id, InstitutionId, InstitutionDonorId),
    CONSTRAINT CK_Standing_Dates CHECK (EndDate IS NULL OR EndDate >= StartDate),
    -- EndDate IS NOT NULL first: on NULL, "EndDate = StartDate" is unknown, which a CHECK lets through.
    CONSTRAINT CK_Standing_Shape CHECK (
        (Pattern = 'Once'    AND EndDate IS NOT NULL AND EndDate = StartDate AND MonthInterval = 1 AND WeekDays IS NULL     AND MonthDays IS NULL     AND YearMonth IS NULL     AND YearDay IS NULL)
     OR (Pattern = 'Daily'                                                   AND MonthInterval = 1 AND WeekDays IS NULL     AND MonthDays IS NULL     AND YearMonth IS NULL     AND YearDay IS NULL)
     OR (Pattern = 'Weekly'                                                  AND MonthInterval = 1 AND WeekDays IS NOT NULL AND MonthDays IS NULL     AND YearMonth IS NULL     AND YearDay IS NULL)
     OR (Pattern = 'Monthly'                                                                       AND WeekDays IS NULL     AND MonthDays IS NOT NULL AND YearMonth IS NULL     AND YearDay IS NULL)
     OR (Pattern = 'Yearly'                                                  AND MonthInterval = 1 AND WeekDays IS NULL     AND MonthDays IS NULL     AND YearMonth IS NOT NULL AND YearDay IS NOT NULL)),
    -- A yearly day that some year has: 29 February yes (skipped in common years, Q3), 31 April never.
    CONSTRAINT CK_Standing_YearDate CHECK (YearMonth IS NULL OR YearDay <= CASE WHEN YearMonth = 2 THEN 29 WHEN YearMonth = 4 OR YearMonth = 6 OR YearMonth = 9 OR YearMonth = 11 THEN 30 ELSE 31 END),
    CONSTRAINT CK_Standing_Ended CHECK (
        (Status = 'Active' AND EndedAt IS NULL     AND EndedBy IS NULL)
     OR (Status = 'Ended'  AND EndedAt IS NOT NULL AND EndedBy IS NOT NULL))
);
GO

CREATE UNIQUE INDEX UX_Standing_PublicId ON booking.StandingSponsorships(PublicId);
GO

/* the nightly job's work list, and the donor book's sponsorships per donor */
CREATE INDEX IX_Standing_Active ON booking.StandingSponsorships(Status, MealSlotId) INCLUDE (InstitutionId, InstitutionDonorId);
GO
CREATE INDEX IX_Standing_Donor ON booking.StandingSponsorships(InstitutionDonorId, Status);
GO
