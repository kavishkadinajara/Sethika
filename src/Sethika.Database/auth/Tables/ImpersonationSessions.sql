/* Audited "view as user" for platform support. Every action during the session is tagged in AuditLogs. */
CREATE TABLE auth.ImpersonationSessions (
    Id             INT           IDENTITY(1,1) PRIMARY KEY,
    AdminUserId    UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    TargetUserId   UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InstitutionId  INT           NULL REFERENCES org.Institutions(Id),
    Reason         NVARCHAR(300) NOT NULL,
    TicketRef      VARCHAR(50)   NULL,
    IsReadOnly     BIT           NOT NULL DEFAULT 1,
    StartedAt      DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt      DATETIME2(3)  NOT NULL,
    EndedAt        DATETIME2(3)  NULL,
    CONSTRAINT CK_Impersonation_NotSelf CHECK (AdminUserId <> TargetUserId)
);
GO
