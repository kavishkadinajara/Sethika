CREATE TABLE booking.Waitlist (
    Id             BIGINT       IDENTITY(1,1) PRIMARY KEY,
    SlotInstanceId BIGINT       NOT NULL REFERENCES booking.SlotInstances(Id),
    DonorUserId    UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    Position       SMALLINT     NOT NULL,
    Status         VARCHAR(20)  NOT NULL DEFAULT 'Waiting'
        CONSTRAINT CK_Waitlist_Status CHECK (Status = 'Waiting' OR Status = 'Offered' OR Status = 'Converted' OR Status = 'Expired' OR Status = 'Cancelled'),
    OfferedAt      DATETIME2(3) NULL,
    OfferExpiresAt DATETIME2(3) NULL,
    CreatedAt      DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Waitlist UNIQUE (SlotInstanceId, DonorUserId)
);
GO
