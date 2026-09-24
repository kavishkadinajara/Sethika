/* Memorial dana planner: enter the date of passing once, the rules generate 7th day / 3 months / yearly bookings */
CREATE TABLE booking.MemorialPlans (
    Id                   INT           IDENTITY(1,1) PRIMARY KEY,
    DonorUserId          UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    DeceasedName         NVARCHAR(150) NOT NULL,
    RelationshipLookupId INT           NULL REFERENCES core.LookupValues(Id),
    DateOfPassing        DATE          NOT NULL,
    ReligionLookupId     INT           NOT NULL REFERENCES core.LookupValues(Id),
    PhotoFileId          BIGINT        NULL REFERENCES core.Files(Id),
    Notes                NVARCHAR(500) NULL,
    IsActive             BIT           NOT NULL DEFAULT 1,
    CreatedAt            DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
