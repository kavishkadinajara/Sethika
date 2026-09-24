CREATE TABLE auth.LoginHistory (
    Id            BIGINT        IDENTITY(1,1) PRIMARY KEY,
    UserId        UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    LoginName     NVARCHAR(256) NOT NULL,
    Succeeded     BIT           NOT NULL,
    FailureReason VARCHAR(50)   NULL,
    MfaUsed       BIT           NOT NULL DEFAULT 0,
    IpAddress     VARCHAR(45)   NULL,
    UserAgent     NVARCHAR(400) NULL,
    AttemptedAt   DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX IX_LoginHistory_User_Time ON auth.LoginHistory(UserId, AttemptedAt DESC);
GO
