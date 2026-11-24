/* A donor's yearly booking (plan 07, H2): a promise to try every year, not a held place. Each year's
   booking is an ordinary booking made by the engine on the first day the slot takes bookings for that
   date (H3); LastGeneratedYear is the last year tried, booked or not, so each year is tried once.
   PublicId and FulfilmentMode came last (C.1): added at the end, they deploy as ALTER TABLE ADD; in
   the middle, DacFx would rebuild the table, which Row-Level Security does not allow. */
CREATE TABLE booking.RecurringBookings (
    Id                INT           IDENTITY(1,1) PRIMARY KEY,
    DonorUserId       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InstitutionId     INT           NOT NULL,
    MealSlotId        INT           NOT NULL,
    MemorialPlanId    INT           NULL REFERENCES booking.MemorialPlans(Id),
    RecurMonth        TINYINT       NOT NULL
        CONSTRAINT CK_Recurring_RecurMonth CHECK (RecurMonth >= 1 AND RecurMonth <= 12),
    RecurDay          TINYINT       NOT NULL
        CONSTRAINT CK_Recurring_RecurDay CHECK (RecurDay >= 1 AND RecurDay <= 31),
    StartYear         SMALLINT      NOT NULL,
    EndYear           SMALLINT      NULL,
    OccasionLookupId  INT           NULL REFERENCES core.LookupValues(Id),
    InMemoryOf        NVARCHAR(200) NULL,
    Headcount         SMALLINT      NOT NULL
        CONSTRAINT CK_Recurring_Headcount CHECK (Headcount > 0),
    LastGeneratedYear SMALLINT      NULL,
    IsActive          BIT           NOT NULL DEFAULT 1,
    CreatedAt         DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    PublicId          UNIQUEIDENTIFIER NOT NULL,
    FulfilmentMode    VARCHAR(20)   NOT NULL
        CONSTRAINT CK_Recurring_FulfilmentMode CHECK (FulfilmentMode = 'DonorBrings' OR FulfilmentMode = 'InstitutionPrepares' OR FulfilmentMode = 'Caterer'),
    CONSTRAINT FK_Recurring_SlotTenant FOREIGN KEY (MealSlotId, InstitutionId) REFERENCES booking.MealSlots(Id, InstitutionId),
    CONSTRAINT CK_Recurring_Years CHECK (EndYear IS NULL OR EndYear >= StartYear)
);
GO

CREATE UNIQUE INDEX UX_Recurring_PublicId ON booking.RecurringBookings(PublicId);
GO

/* one yearly booking per donor, meal and day of the year; a stopped one may be started again */
CREATE UNIQUE INDEX UX_Recurring_DonorSlotDay ON booking.RecurringBookings(DonorUserId, MealSlotId, RecurMonth, RecurDay) WHERE IsActive = 1;
GO
