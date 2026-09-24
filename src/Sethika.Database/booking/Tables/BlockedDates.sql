CREATE TABLE booking.BlockedDates (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    MealSlotId    INT           NULL,
    DateFrom      DATE          NOT NULL,
    DateTo        DATE          NOT NULL,
    Reason        NVARCHAR(200) NOT NULL,
    SpecialDayId  INT           NULL REFERENCES core.SpecialDays(Id),
    CreatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CreatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Blocked_SlotTenant FOREIGN KEY (MealSlotId, InstitutionId) REFERENCES booking.MealSlots(Id, InstitutionId),
    CONSTRAINT CK_BlockedDates_Range CHECK (DateTo >= DateFrom)
);
GO
