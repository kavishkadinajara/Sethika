CREATE TABLE org.RegistrationRequestClarifications (
    Id          INT            IDENTITY(1,1) PRIMARY KEY,
    RequestId   INT            NOT NULL REFERENCES org.RegistrationRequests(Id) ON DELETE CASCADE,
    AskedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    Question    NVARCHAR(1000) NOT NULL,
    AskedAt     DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
    Response    NVARCHAR(2000) NULL,
    ResponseFileId BIGINT      NULL REFERENCES core.Files(Id),
    RespondedBy UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    RespondedAt DATETIME2(3)   NULL
);
GO
