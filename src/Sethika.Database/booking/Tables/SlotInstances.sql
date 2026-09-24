/* One row per slot per date, created on first booking (or pre-generated for a horizon).
   Counters are changed with a single conditional UPDATE — the CHECKs make overbooking impossible. */
CREATE TABLE booking.SlotInstances (
    Id               BIGINT       IDENTITY(1,1) PRIMARY KEY,
    InstitutionId    INT          NOT NULL,
    MealSlotId       INT          NOT NULL,
    SlotDate         DATE         NOT NULL,
    MaxSponsors      TINYINT      NOT NULL,          -- snapshot of MealSlots at creation
    MaxHeadcount     SMALLINT     NOT NULL,
    BookedSponsors   TINYINT      NOT NULL DEFAULT 0,
    BookedHeadcount  SMALLINT     NOT NULL DEFAULT 0,
    Status           VARCHAR(10)  NOT NULL DEFAULT 'Open'
        CONSTRAINT CK_SlotInst_Status CHECK (Status = 'Open' OR Status = 'Full' OR Status = 'Blocked' OR Status = 'Closed'),
    RowVersion       ROWVERSION,
    CONSTRAINT FK_SlotInst_SlotTenant FOREIGN KEY (MealSlotId, InstitutionId) REFERENCES booking.MealSlots(Id, InstitutionId),
    CONSTRAINT FK_SlotInst_Institution FOREIGN KEY (InstitutionId) REFERENCES org.Institutions(Id),
    CONSTRAINT UQ_SlotInst_SlotDate UNIQUE (MealSlotId, SlotDate),
    CONSTRAINT UQ_SlotInst_Tenant   UNIQUE (Id, InstitutionId),
    CONSTRAINT CK_SlotInst_Sponsors  CHECK (BookedSponsors  >= 0 AND BookedSponsors  <= MaxSponsors),
    CONSTRAINT CK_SlotInst_Headcount CHECK (BookedHeadcount >= 0 AND BookedHeadcount <= MaxHeadcount)
);
GO

CREATE INDEX IX_SlotInst_Date ON booking.SlotInstances(SlotDate, Status) INCLUDE (InstitutionId, MealSlotId, BookedSponsors, MaxSponsors);
GO
