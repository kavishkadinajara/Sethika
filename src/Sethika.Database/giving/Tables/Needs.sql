CREATE TABLE giving.Needs (
    Id               INT           IDENTITY(1,1) PRIMARY KEY,
    PublicId         UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() UNIQUE,
    InstitutionId    INT           NOT NULL REFERENCES org.Institutions(Id),
    Title            NVARCHAR(150) NOT NULL,
    Description      NVARCHAR(1000) NULL,
    CategoryLookupId INT           NOT NULL REFERENCES core.LookupValues(Id),
    UnitLookupId     INT           NOT NULL REFERENCES core.LookupValues(Id),
    QuantityNeeded   DECIMAL(10,2) NOT NULL
        CONSTRAINT CK_Needs_QuantityNeeded CHECK (QuantityNeeded > 0),
    QuantityPledged  DECIMAL(10,2) NOT NULL DEFAULT 0,
    QuantityReceived DECIMAL(10,2) NOT NULL DEFAULT 0,
    Priority         VARCHAR(10)   NOT NULL DEFAULT 'Normal'
        CONSTRAINT CK_Needs_Priority CHECK (Priority = 'Low' OR Priority = 'Normal' OR Priority = 'Urgent'),
    Deadline         DATE          NULL,
    ImageFileId      BIGINT        NULL REFERENCES core.Files(Id),
    Status           VARCHAR(20)   NOT NULL DEFAULT 'Draft'
        CONSTRAINT CK_Needs_Status CHECK (Status = 'Draft' OR Status = 'Open' OR Status = 'Fulfilled' OR Status = 'Closed' OR Status = 'Cancelled'),
    CreatedBy        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CreatedAt        DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt        DATETIME2(3)  NULL,
    UpdatedBy        UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    RowVersion       ROWVERSION,
    CONSTRAINT UQ_Needs_Tenant UNIQUE (Id, InstitutionId),
    CONSTRAINT CK_Needs_Pledged  CHECK (QuantityPledged >= 0 AND QuantityPledged <= QuantityNeeded),
    CONSTRAINT CK_Needs_Received CHECK (QuantityReceived >= 0 AND QuantityReceived <= QuantityNeeded)
);
GO

CREATE INDEX IX_Needs_Open ON giving.Needs(Status, Priority, Deadline) WHERE Status = 'Open';
GO
