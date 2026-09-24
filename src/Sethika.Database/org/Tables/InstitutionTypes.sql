CREATE TABLE org.InstitutionTypes (
    Id               SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    Code             VARCHAR(30)   NOT NULL UNIQUE,
    Name             NVARCHAR(100) NOT NULL,
    NameSi           NVARCHAR(100) NULL,
    NameTa           NVARCHAR(100) NULL,
    Category         VARCHAR(20)   NOT NULL
        CONSTRAINT CK_InstType_Category CHECK (Category = 'Religious' OR Category = 'CareHome'),
    ReligionLookupId INT           NULL REFERENCES core.LookupValues(Id),
    MealSlotLabel    NVARCHAR(50)  NOT NULL DEFAULT N'Meal',
    IsActive         BIT           NOT NULL DEFAULT 1
);
GO
