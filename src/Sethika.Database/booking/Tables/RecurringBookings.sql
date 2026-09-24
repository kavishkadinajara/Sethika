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
    CONSTRAINT FK_Recurring_SlotTenant FOREIGN KEY (MealSlotId, InstitutionId) REFERENCES booking.MealSlots(Id, InstitutionId),
    CONSTRAINT CK_Recurring_Years CHECK (EndYear IS NULL OR EndYear >= StartYear)
);
GO
