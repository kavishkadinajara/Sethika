/* Co-sponsors: siblings share a parent's memorial dana — invite by link (plan 07, H16-H19).
   The donor makes a link; the person who opens it joins or declines. Sethika texts no one: Phone
   stays empty. InviteTokenHash is the SHA-256 of the link's secret, never the secret itself. */
CREATE TABLE booking.BookingContributors (
    Id               BIGINT        IDENTITY(1,1) PRIMARY KEY,
    BookingId        BIGINT        NOT NULL REFERENCES booking.Bookings(Id) ON DELETE CASCADE,
    UserId           UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    Name             NVARCHAR(150) NOT NULL,
    Phone            VARCHAR(20)   NULL,
    ContributionNote NVARCHAR(200) NULL,        -- "brings dessert", "Rs 5,000"
    InviteTokenHash  CHAR(64)      NULL UNIQUE,
    Status           VARCHAR(10)   NOT NULL DEFAULT 'Invited'
        CONSTRAINT CK_Contributor_Status CHECK (Status = 'Invited' OR Status = 'Joined' OR Status = 'Declined'),
    JoinedAt         DATETIME2(3)  NULL,
    -- Added by plan 07 (H18) at the end of the table, so the deploy is an ALTER, never a rebuild of
    -- a table under RLS. The table was empty everywhere when they were added.
    InstitutionId    INT           NOT NULL,
    InvitedBy        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Contributor_InvitedBy REFERENCES auth.Users(Id),
    PublicId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_Contributor_PublicId UNIQUE,
    InvitedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_Contributor_InvitedAt DEFAULT SYSUTCDATETIME(),
    DeclinedAt       DATETIME2(3)  NULL,
    CONSTRAINT FK_Contributor_BookingTenant FOREIGN KEY (BookingId, InstitutionId) REFERENCES booking.Bookings(Id, InstitutionId),
    -- Joined has its who and when; Declined has its who and when; Invited has neither.
    CONSTRAINT CK_Contributor_Answer CHECK (
        (Status = 'Invited'  AND UserId IS NULL     AND JoinedAt IS NULL     AND DeclinedAt IS NULL)
     OR (Status = 'Joined'   AND UserId IS NOT NULL AND JoinedAt IS NOT NULL AND DeclinedAt IS NULL)
     OR (Status = 'Declined' AND UserId IS NOT NULL AND JoinedAt IS NULL     AND DeclinedAt IS NOT NULL)),
    -- The donor cannot be their own co-sponsor.
    CONSTRAINT CK_Contributor_NotTheDonor CHECK (UserId IS NULL OR UserId <> InvitedBy)
);
GO

/* One person joins a booking once, whichever of its links they open (H17). */
CREATE UNIQUE INDEX UX_Contributors_JoinedOnce ON booking.BookingContributors(BookingId, UserId) WHERE Status = 'Joined';
GO

/* A booking's invitations (the donor's list, the limit of 10), and "Shared with me". */
CREATE INDEX IX_Contributors_Booking ON booking.BookingContributors(BookingId) INCLUDE (Status);
GO
CREATE INDEX IX_Contributors_User ON booking.BookingContributors(UserId, Status) WHERE UserId IS NOT NULL;
GO
