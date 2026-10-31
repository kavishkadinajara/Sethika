/* A religious service an institution offers (Phase E, promise 1). Institution-owned; nothing is
   seeded per faith (V4). Public read, tenant-only write, like MealSlots. */
CREATE TABLE booking.ServiceTypes (
    Id              INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId   INT           NOT NULL REFERENCES org.Institutions(Id),
    Name            NVARCHAR(100) NOT NULL,
    NameSi          NVARCHAR(100) NULL,
    NameTa          NVARCHAR(100) NULL,
    Description     NVARCHAR(300) NULL,
    DurationMinutes SMALLINT      NOT NULL,
    BufferMinutes   SMALLINT      NOT NULL DEFAULT 0,   -- travel time either side of an at-home service (V5)
    LeadersRequired TINYINT       NOT NULL DEFAULT 1,
    LocationMode    VARCHAR(15)   NOT NULL DEFAULT 'AtInstitution'
        CONSTRAINT CK_ServiceTypes_LocationMode CHECK (LocationMode = 'AtInstitution' OR LocationMode = 'AtDonorPlace' OR LocationMode = 'Either'),
    MinAdvanceDays  SMALLINT      NOT NULL DEFAULT 1,
    MaxAdvanceDays  SMALLINT      NOT NULL DEFAULT 180,
    IsActive        BIT           NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    UpdatedAt       DATETIME2(3)  NULL,
    UpdatedBy       UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    RowVersion      ROWVERSION,
    PublicId        UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT CK_ServiceTypes_Duration CHECK (DurationMinutes >= 15 AND DurationMinutes <= 720 AND DurationMinutes % 15 = 0),
    CONSTRAINT CK_ServiceTypes_Buffer   CHECK (BufferMinutes >= 0 AND BufferMinutes <= 180 AND BufferMinutes % 15 = 0),
    CONSTRAINT CK_ServiceTypes_Leaders  CHECK (LeadersRequired >= 1 AND LeadersRequired <= 20),
    CONSTRAINT CK_ServiceTypes_Advance  CHECK (MaxAdvanceDays >= MinAdvanceDays AND MinAdvanceDays >= 0),
    CONSTRAINT UQ_ServiceTypes_Name   UNIQUE (InstitutionId, Name),
    CONSTRAINT UQ_ServiceTypes_Tenant UNIQUE (Id, InstitutionId)
);
GO

CREATE UNIQUE INDEX UX_ServiceTypes_PublicId ON booking.ServiceTypes(PublicId);
GO
