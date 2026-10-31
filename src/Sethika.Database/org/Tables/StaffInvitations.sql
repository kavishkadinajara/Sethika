/*  A person invited to an institution's staff (plan 04, X1-X5). Always an existing account, found
    by phone when inviting (InviteeUserId), so the invitee reads and answers it in their own
    session: RLS lets the institution and the invitee see it (fn_TenantOrOwner, as Bookings).
    TokenHash is kept for a later texted link; this slice issues a random one and never sends it. */
CREATE TABLE org.StaffInvitations (
    Id             INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId  INT           NOT NULL REFERENCES org.Institutions(Id),
    InviteeName    NVARCHAR(150) NOT NULL,
    InviteePhone   VARCHAR(20)   NULL,
    InviteeEmail   NVARCHAR(256) NULL,
    RoleId         INT           NOT NULL,
    TokenHash      CHAR(64)      NOT NULL UNIQUE,
    Status         VARCHAR(20)   NOT NULL DEFAULT 'Pending'
        CONSTRAINT CK_Invite_Status CHECK (Status = 'Pending' OR Status = 'Accepted' OR Status = 'Declined' OR Status = 'Expired' OR Status = 'Revoked'),
    InvitedBy      UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InvitedAt      DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt      DATETIME2(3)  NOT NULL,
    AcceptedUserId UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    AcceptedAt     DATETIME2(3)  NULL,
    -- Who answered or cancelled it, and when: the invitee (Declined) or the institution (Revoked).
    ClosedBy       UNIQUEIDENTIFIER NULL CONSTRAINT FK_Invite_ClosedBy REFERENCES auth.Users(Id),
    ClosedAt       DATETIME2(3)  NULL,
    -- Added by plan 04 at the end of the table, so the deploy is an ALTER, never a rebuild of a
    -- table under RLS.
    PublicId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Invite_PublicId DEFAULT NEWSEQUENTIALID()
        CONSTRAINT UQ_Invite_PublicId UNIQUE,
    InviteeUserId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Invite_Invitee REFERENCES auth.Users(Id),
    CONSTRAINT FK_Invite_RoleTenant FOREIGN KEY (RoleId, InstitutionId) REFERENCES auth.Roles(Id, InstitutionId),
    CONSTRAINT CK_Invite_Contact CHECK (InviteePhone IS NOT NULL OR InviteeEmail IS NOT NULL),
    -- Accepted has its who and when, and nothing else does.
    CONSTRAINT CK_Invite_Accepted CHECK (
        (Status = 'Accepted' AND AcceptedUserId IS NOT NULL AND AcceptedAt IS NOT NULL)
        OR (Status <> 'Accepted' AND AcceptedUserId IS NULL AND AcceptedAt IS NULL)),
    -- Only the person invited can be the one who accepted.
    CONSTRAINT CK_Invite_AcceptedByInvitee CHECK (AcceptedUserId IS NULL OR AcceptedUserId = InviteeUserId)
);
GO

/* One open invitation per person and institution: inviting again while one waits is a 409. */
CREATE UNIQUE INDEX UX_Invite_Pending ON org.StaffInvitations(InstitutionId, InviteeUserId) WHERE Status = 'Pending';
GO

/* The invitee's own list (/api/me/invitations). */
CREATE INDEX IX_Invite_Invitee ON org.StaffInvitations(InviteeUserId, Status) INCLUDE (ExpiresAt);
GO
