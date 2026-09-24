/* Institution's own donor book (CRM). Links phone/walk-in donors and online users into one history. */
CREATE TABLE org.InstitutionDonors (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    UserId        UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    FullName      NVARCHAR(150) NOT NULL,
    Phone         VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NOT NULL,
    Email         NVARCHAR(256) NULL,
    Address       NVARCHAR(300) NULL,
    Notes         NVARCHAR(500) NULL,
    FirstGivenAt  DATE          NULL,
    LastGivenAt   DATE          NULL,
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_InstDonor_Phone UNIQUE (InstitutionId, Phone),
    CONSTRAINT UQ_InstDonor_Tenant UNIQUE (Id, InstitutionId)          -- target for tenant-consistent FKs
);
GO
