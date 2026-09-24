CREATE TABLE org.InstitutionSettings (
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    SettingKey    VARCHAR(100)  NOT NULL,   -- AUTO_APPROVE_BOOKINGS, CANCEL_WINDOW_HOURS, REMINDER_DAYS_BEFORE ...
    SettingValue  NVARCHAR(500) NOT NULL,
    UpdatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    UpdatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (InstitutionId, SettingKey)
);
GO
