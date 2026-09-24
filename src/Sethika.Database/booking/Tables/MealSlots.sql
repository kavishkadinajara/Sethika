CREATE TABLE booking.MealSlots (
    Id                   INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId        INT           NOT NULL REFERENCES org.Institutions(Id),
    Name                 NVARCHAR(100) NOT NULL,
    NameSi               NVARCHAR(100) NULL,
    NameTa               NVARCHAR(100) NULL,
    StartTime            TIME(0)       NOT NULL,
    EndTime              TIME(0)       NOT NULL,
    DaysOfWeekMask       TINYINT       NOT NULL DEFAULT 127
        CONSTRAINT CK_MealSlots_DaysOfWeekMask CHECK (DaysOfWeekMask >= 1 AND DaysOfWeekMask <= 127),
    BookingMode          VARCHAR(10)   NOT NULL DEFAULT 'Exclusive'
        CONSTRAINT CK_MealSlots_BookingMode CHECK (BookingMode = 'Exclusive' OR BookingMode = 'Shared'),
    MaxSponsors          TINYINT       NOT NULL DEFAULT 1,       -- 1 for Exclusive; e.g. 4 families share a lunch
    MinHeadcount         SMALLINT      NOT NULL DEFAULT 1,
    MaxHeadcount         SMALLINT      NOT NULL,
    EstimatedCostPerHead DECIMAL(10,2) NULL,                     -- helps donors budget
    MinAdvanceDays       SMALLINT      NOT NULL DEFAULT 1,
    MaxAdvanceDays       SMALLINT      NOT NULL DEFAULT 365,
    RequiresApproval     BIT           NOT NULL DEFAULT 1,
    AllowedFulfilment    VARCHAR(40)   NOT NULL DEFAULT 'DonorBrings,InstitutionPrepares',
    SortOrder            SMALLINT      NOT NULL DEFAULT 0,
    IsActive             BIT           NOT NULL DEFAULT 1,
    CreatedAt            DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy            UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    UpdatedAt            DATETIME2(3)  NULL,
    UpdatedBy            UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    RowVersion           ROWVERSION,
    CONSTRAINT CK_MealSlots_Time      CHECK (EndTime > StartTime),
    CONSTRAINT CK_MealSlots_Headcount CHECK (MaxHeadcount >= MinHeadcount AND MinHeadcount > 0),
    CONSTRAINT CK_MealSlots_Advance   CHECK (MaxAdvanceDays >= MinAdvanceDays AND MinAdvanceDays >= 0),
    CONSTRAINT CK_MealSlots_Sponsors  CHECK ((BookingMode = 'Exclusive' AND MaxSponsors = 1) OR (BookingMode = 'Shared' AND MaxSponsors >= 2 AND MaxSponsors <= 20)),
    CONSTRAINT UQ_MealSlots_Name   UNIQUE (InstitutionId, Name),
    CONSTRAINT UQ_MealSlots_Tenant UNIQUE (Id, InstitutionId)
);
GO
