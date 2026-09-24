CREATE TABLE org.RegistrationRequestStatusHistory (
    Id         BIGINT       IDENTITY(1,1) PRIMARY KEY,
    RequestId  INT          NOT NULL REFERENCES org.RegistrationRequests(Id) ON DELETE CASCADE,
    FromStatus VARCHAR(20)  NULL,
    ToStatus   VARCHAR(20)  NOT NULL,
    ChangedBy  UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ChangedAt  DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Comment    NVARCHAR(1000) NULL
);
GO
