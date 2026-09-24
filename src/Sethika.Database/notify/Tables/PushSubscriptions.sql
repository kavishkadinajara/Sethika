CREATE TABLE notify.PushSubscriptions (       -- web push for the PWA
    Id        INT           IDENTITY(1,1) PRIMARY KEY,
    UserId    UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    Endpoint  NVARCHAR(500) NOT NULL UNIQUE,
    P256dh    VARCHAR(200)  NOT NULL,
    AuthKey   VARCHAR(100)  NOT NULL,
    CreatedAt DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
