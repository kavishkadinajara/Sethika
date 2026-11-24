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
    -- Added by plan 09 (M13, M15) at the end of the table, so the deploy is an ALTER. Built in D.2.
    CancelledAt          DATETIME2(3)  NULL,
    CancelledBy          UNIQUEIDENTIFIER NULL CONSTRAINT FK_Pledges_CancelledBy REFERENCES auth.Users(Id),
    CancelReason         NVARCHAR(300) NULL,
    ReceivedQuantity     DECIMAL(10,2) NULL,
    RowVersion           ROWVERSION,
    -- Added by D.2 (M31): a pledge is addressed in URLs by this, never by Id. v7, set in C#. The table
    -- was empty everywhere when it was added, so NOT NULL needs no default.
    PublicId             UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT UQ_Pledges_PublicId UNIQUE (PublicId),
    CONSTRAINT CK_Pledges_Cancelled CHECK ((Status = 'Cancelled' AND CancelledAt IS NOT NULL AND CancelledBy IS NOT NULL)
                                        OR (Status <> 'Cancelled' AND CancelledAt IS NULL AND CancelledBy IS NULL AND CancelReason IS NULL)),
    -- Delivered means who received how much, at most what was pledged (M15).
    CONSTRAINT CK_Pledges_Received CHECK ((Status = 'Delivered' AND ReceivedBy IS NOT NULL AND ReceivedAt IS NOT NULL
                                           AND ReceivedQuantity IS NOT NULL AND ReceivedQuantity > 0 AND ReceivedQuantity <= Quantity)
                                       OR (Status <> 'Delivered' AND ReceivedQuantity IS NULL)),
    CONSTRAINT FK_Pledges_NeedTenant FOREIGN KEY (NeedId, InstitutionId) REFERENCES giving.Needs(Id, InstitutionId),
    CONSTRAINT UQ_Pledges_Tenant UNIQUE (Id, InstitutionId)
);
GO

-- A donor's open pledges on a need (at most 5, M13) and My giving's list.
CREATE INDEX IX_Pledges_DonorOpen ON giving.Pledges(DonorUserId, NeedId) WHERE Status = 'Pledged';
GO
