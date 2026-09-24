/* Poya days, public holidays, religious festivals — drives search filters,
   "suggest blocking" prompts and demand forecasting */
CREATE TABLE core.SpecialDays (
    Id               INT           IDENTITY(1,1) PRIMARY KEY,
    CalendarDate     DATE          NOT NULL,
    Code             VARCHAR(40)   NOT NULL,          -- POYA_VESAK, THAI_PONGAL, RAMADAN_START, CHRISTMAS
    Name             NVARCHAR(150) NOT NULL,
    NameSi           NVARCHAR(150) NULL,
    NameTa           NVARCHAR(150) NULL,
    DayType          VARCHAR(20)   NOT NULL
        CONSTRAINT CK_SpecialDays_DayType CHECK (DayType = 'Poya' OR DayType = 'PublicHoliday' OR DayType = 'ReligiousFestival' OR DayType = 'Observance'),
    ReligionLookupId INT           NULL REFERENCES core.LookupValues(Id),
    CONSTRAINT UQ_SpecialDays UNIQUE (CalendarDate, Code)
);
GO
