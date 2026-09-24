/* Co-sponsors: siblings share a parent's memorial dana — invite by link */
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
    JoinedAt         DATETIME2(3)  NULL
);
GO
