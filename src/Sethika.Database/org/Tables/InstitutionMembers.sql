CREATE TABLE org.InstitutionMembers (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    UserId        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    Designation   NVARCHAR(100) NULL,
    IsOwner       BIT           NOT NULL DEFAULT 0,
    Status        VARCHAR(20)   NOT NULL DEFAULT 'Active'
        CONSTRAINT CK_InstMember_Status CHECK (Status = 'Active' OR Status = 'Suspended' OR Status = 'Removed'),
    JoinedAt      DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    AddedBy       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    RemovedAt     DATETIME2(3)  NULL,
    RemovedBy     UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    CONSTRAINT UQ_InstMember UNIQUE (InstitutionId, UserId)
);
GO

CREATE UNIQUE INDEX UX_InstMember_Owner ON org.InstitutionMembers(InstitutionId) WHERE IsOwner = 1 AND Status = 'Active';
GO
