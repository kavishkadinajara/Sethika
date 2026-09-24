CREATE TABLE auth.PasswordHistory (          -- block reuse of the last N passwords
    Id           BIGINT        IDENTITY(1,1) PRIMARY KEY,
    UserId       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    PasswordHash NVARCHAR(MAX) NOT NULL,
    CreatedAt    DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
