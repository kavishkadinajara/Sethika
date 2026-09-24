CREATE TABLE auth.UserMfaMethods (
    Id              INT           IDENTITY(1,1) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    Method          VARCHAR(10)   NOT NULL
        CONSTRAINT CK_UserMfa_Method CHECK (Method = 'TOTP' OR Method = 'SMS' OR Method = 'Email'),
    SecretEncrypted VARBINARY(512) NULL,      -- TOTP secret, encrypted by the app (Data Protection API)
    IsPrimary       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    LastUsedAt      DATETIME2(3)  NULL,
    CONSTRAINT UQ_UserMfa UNIQUE (UserId, Method)
);
GO
