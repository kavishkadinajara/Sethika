CREATE TABLE notify.MobileDevices (           -- a phone's Expo push token (plan F, F4). A donor's device,
    Id             INT              IDENTITY(1,1) PRIMARY KEY,   -- so no InstitutionId and no RLS, like auth.RefreshTokens
    UserId         UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ExpoPushToken  VARCHAR(200)     NOT NULL CONSTRAINT UQ_MobileDevices_Token UNIQUE,
    Platform       VARCHAR(10)      NOT NULL
        CONSTRAINT CK_MobileDevices_Platform CHECK (Platform = 'Android' OR Platform = 'iOS'),
    AppVersion     VARCHAR(20)      NULL,
    CreatedAt      DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    LastSeenAt     DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    DisabledAt     DATETIME2(3)     NULL,     -- the row stays: its Id is in the push dedup keys (F28)
    DisabledReason NVARCHAR(100)    NULL
);
GO

CREATE INDEX IX_MobileDevices_User ON notify.MobileDevices(UserId) WHERE DisabledAt IS NULL;
GO
