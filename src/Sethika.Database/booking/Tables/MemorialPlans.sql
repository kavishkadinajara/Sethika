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
    CreatedAt            DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    /* how the donor's screens and the API name a plan (plan 07); last, so adding it never rebuilds a table under RLS */
    PublicId             UNIQUEIDENTIFIER NOT NULL
);
GO

CREATE UNIQUE INDEX UX_MemorialPlans_PublicId ON booking.MemorialPlans(PublicId);
GO

/* a donor's plans, and the reminder job's active ones */
CREATE INDEX IX_MemorialPlans_Donor ON booking.MemorialPlans(DonorUserId, IsActive);
GO
