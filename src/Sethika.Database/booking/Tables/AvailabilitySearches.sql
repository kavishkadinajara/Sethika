/* Demand signals: searches that returned few/no results => admin heat-map of unmet demand */
CREATE TABLE booking.AvailabilitySearches (
    Id                BIGINT       IDENTITY(1,1) PRIMARY KEY,
    SearchedAt        DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UserId            UNIQUEIDENTIFIER NULL,
    TargetDate        DATE         NOT NULL,
    InstitutionTypeId SMALLINT     NULL,
    DistrictId        SMALLINT     NULL,
    RadiusKm          SMALLINT     NULL,
    ResultCount       SMALLINT     NOT NULL
);
GO

CREATE INDEX IX_Searches_Demand ON booking.AvailabilitySearches(TargetDate, DistrictId) INCLUDE (ResultCount);
GO
