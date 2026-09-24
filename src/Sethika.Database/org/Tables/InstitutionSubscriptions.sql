CREATE TABLE org.InstitutionSubscriptions (
    Id            INT          IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT          NOT NULL REFERENCES org.Institutions(Id),
    PlanId        SMALLINT     NOT NULL REFERENCES org.Plans(Id),
    StartsAt      DATE         NOT NULL,
    EndsAt        DATE         NULL,
    Status        VARCHAR(15)  NOT NULL DEFAULT 'Active'
        CONSTRAINT CK_Subs_Status CHECK (Status = 'Trial' OR Status = 'Active' OR Status = 'PastDue' OR Status = 'Cancelled' OR Status = 'Expired'),
    SponsoredBy   NVARCHAR(150) NULL,      -- a CSR partner can pay for an institution's plan
    CreatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CreatedAt     DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Subs_Dates CHECK (EndsAt IS NULL OR EndsAt > StartsAt)
);
GO

CREATE UNIQUE INDEX UX_Subs_Current ON org.InstitutionSubscriptions(InstitutionId) WHERE Status IN ('Trial','Active','PastDue');
GO
