/* "Tell me when a lunch slot near Galle frees up between 10–20 March" */
CREATE TABLE booking.AvailabilityAlerts (
    Id                INT           IDENTITY(1,1) PRIMARY KEY,
    UserId            UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    DateFrom          DATE          NOT NULL,
    DateTo            DATE          NOT NULL,
    InstitutionTypeId SMALLINT      NULL REFERENCES org.InstitutionTypes(Id),
    DistrictId        SMALLINT      NULL REFERENCES core.Districts(Id),
    Latitude          DECIMAL(9,6)  NULL,
    Longitude         DECIMAL(9,6)  NULL,
    RadiusKm          SMALLINT      NULL
        CONSTRAINT CK_Alert_RadiusKm CHECK (RadiusKm >= 1 AND RadiusKm <= 200),
    MinHeadcount      SMALLINT      NULL,
    IsActive          BIT           NOT NULL DEFAULT 1,
    LastNotifiedAt    DATETIME2(3)  NULL,
    CreatedAt         DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Alert_Range CHECK (DateTo >= DateFrom),
    CONSTRAINT CK_Alert_Where CHECK (DistrictId IS NOT NULL OR (Latitude IS NOT NULL AND Longitude IS NOT NULL AND RadiusKm IS NOT NULL))
);
GO
