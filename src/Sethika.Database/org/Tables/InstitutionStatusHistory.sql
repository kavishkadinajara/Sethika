CREATE TABLE org.InstitutionStatusHistory (
    Id            BIGINT       IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT          NOT NULL REFERENCES org.Institutions(Id),
    FromStatus    VARCHAR(20)  NULL,
    ToStatus      VARCHAR(20)  NOT NULL,
    Reason        NVARCHAR(1000) NULL,
    ChangedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ChangedAt     DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
