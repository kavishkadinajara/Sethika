CREATE TABLE auth.PasswordHistory (          -- block reuse of the last N passwords
    Id           BIGINT        IDENTITY(1,1) PRIMARY KEY,
    UserId       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    PasswordHash NVARCHAR(MAX) NOT NULL,
    CreatedAt    DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- A password change or reset reads one user's newest few rows.
CREATE INDEX IX_PasswordHistory_User_Time ON auth.PasswordHistory(UserId, CreatedAt DESC);
GO
