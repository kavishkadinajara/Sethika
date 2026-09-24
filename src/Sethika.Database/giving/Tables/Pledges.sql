CREATE TABLE giving.Pledges (
    Id                   INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId        INT           NOT NULL,
    NeedId               INT           NOT NULL,
    DonorUserId          UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    Quantity             DECIMAL(10,2) NOT NULL
        CONSTRAINT CK_Pledges_Quantity CHECK (Quantity > 0),
    ExpectedDeliveryDate DATE          NULL,
    Status               VARCHAR(20)   NOT NULL DEFAULT 'Pledged'
        CONSTRAINT CK_Pledges_Status CHECK (Status = 'Pledged' OR Status = 'Delivered' OR Status = 'Cancelled'),
    ReceivedBy           UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    ReceivedAt           DATETIME2(3)  NULL,
    CreatedAt            DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Pledges_NeedTenant FOREIGN KEY (NeedId, InstitutionId) REFERENCES giving.Needs(Id, InstitutionId),
    CONSTRAINT UQ_Pledges_Tenant UNIQUE (Id, InstitutionId)
);
GO
