/* Memorial observance rules per religion:
   Buddhist 7th day / 3 months / yearly, Muslim & Christian 40th day, Hindu 31st day ... (seed after confirming) */
CREATE TABLE core.MemorialRules (
    Id               SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    ReligionLookupId INT           NOT NULL REFERENCES core.LookupValues(Id),
    Code             VARCHAR(40)   NOT NULL UNIQUE,
    Name             NVARCHAR(100) NOT NULL,
    NameSi           NVARCHAR(100) NULL,
    NameTa           NVARCHAR(100) NULL,
    OffsetDays       SMALLINT      NOT NULL DEFAULT 0,
    OffsetMonths     SMALLINT      NOT NULL DEFAULT 0,
    RepeatsAnnually  BIT           NOT NULL DEFAULT 0,
    SortOrder        SMALLINT      NOT NULL DEFAULT 0,
    IsActive         BIT           NOT NULL DEFAULT 1
);
GO
