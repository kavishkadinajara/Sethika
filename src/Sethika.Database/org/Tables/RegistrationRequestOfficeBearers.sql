CREATE TABLE org.RegistrationRequestOfficeBearers (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    RequestId   INT           NOT NULL REFERENCES org.RegistrationRequests(Id) ON DELETE CASCADE,
    FullName    NVARCHAR(150) NOT NULL,
    NicNumber   VARCHAR(12)   MASKED WITH (FUNCTION = 'partial(2, "XXXXXXX", 2)') NOT NULL,
    Designation NVARCHAR(100) NOT NULL,
    Phone       VARCHAR(20)   NOT NULL,
    Email       NVARCHAR(256) NULL,
    IsSignatory BIT           NOT NULL DEFAULT 0,
    CONSTRAINT UQ_OfficeBearer_Nic UNIQUE (RequestId, NicNumber)
);
GO

CREATE INDEX IX_OfficeBearer_Nic ON org.RegistrationRequestOfficeBearers(NicNumber);  -- same person on many applications = risk flag
GO
