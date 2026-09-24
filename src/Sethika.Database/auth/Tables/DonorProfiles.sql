CREATE TABLE auth.DonorProfiles (
    UserId             UNIQUEIDENTIFIER PRIMARY KEY REFERENCES auth.Users(Id),
    DistrictId         SMALLINT      NULL REFERENCES core.Districts(Id),
    CountryCode        CHAR(2)       NOT NULL DEFAULT 'LK',
    ReligionLookupId   INT           NULL REFERENCES core.LookupValues(Id),   -- optional, only to suggest memorial rules
    DefaultAnonymous   BIT           NOT NULL DEFAULT 0,
    DefaultDisplayName NVARCHAR(150) NULL
);
GO
