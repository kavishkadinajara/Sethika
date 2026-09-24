CREATE TABLE auth.UserConsents (
    Id              BIGINT       IDENTITY(1,1) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    LegalDocumentId SMALLINT     NOT NULL REFERENCES core.LegalDocuments(Id),
    AcceptedAt      DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IpAddress       VARCHAR(45)  NULL,
    UserAgent       NVARCHAR(400) NULL,
    WithdrawnAt     DATETIME2(3) NULL,
    CONSTRAINT UQ_UserConsents UNIQUE (UserId, LegalDocumentId)
);
GO
