CREATE TABLE org.StaffInvitations (
    Id             INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId  INT           NOT NULL REFERENCES org.Institutions(Id),
    InviteeName    NVARCHAR(150) NOT NULL,
    InviteePhone   VARCHAR(20)   NULL,
    InviteeEmail   NVARCHAR(256) NULL,
    RoleId         INT           NOT NULL,
    TokenHash      CHAR(64)      NOT NULL UNIQUE,
    Status         VARCHAR(20)   NOT NULL DEFAULT 'Pending'
        CONSTRAINT CK_Invite_Status CHECK (Status = 'Pending' OR Status = 'Accepted' OR Status = 'Expired' OR Status = 'Revoked'),
    InvitedBy      UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InvitedAt      DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt      DATETIME2(3)  NOT NULL,
    AcceptedUserId UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    AcceptedAt     DATETIME2(3)  NULL,
    CONSTRAINT FK_Invite_RoleTenant FOREIGN KEY (RoleId, InstitutionId) REFERENCES auth.Roles(Id, InstitutionId),
    CONSTRAINT CK_Invite_Contact CHECK (InviteePhone IS NOT NULL OR InviteeEmail IS NOT NULL)
);
GO
