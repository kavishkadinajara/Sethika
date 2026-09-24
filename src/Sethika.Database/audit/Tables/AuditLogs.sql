CREATE TABLE audit.AuditLogs (
    Id                     BIGINT        IDENTITY(1,1) PRIMARY KEY,
    UserId                 UNIQUEIDENTIFIER NULL,
    ImpersonationSessionId INT           NULL,     -- set when support acted "as" a user
    InstitutionId          INT           NULL,
    Action                 VARCHAR(50)   NOT NULL,
    EntityName             VARCHAR(100)  NOT NULL,
    EntityId               VARCHAR(50)   NOT NULL,
    OldValues              NVARCHAR(MAX) NULL
        CONSTRAINT CK_AuditLogs_OldValues CHECK (OldValues IS NULL OR ISJSON(OldValues) = 1),
    NewValues              NVARCHAR(MAX) NULL
        CONSTRAINT CK_AuditLogs_NewValues CHECK (NewValues IS NULL OR ISJSON(NewValues) = 1),
    IpAddress              VARCHAR(45)   NULL,
    UserAgent              NVARCHAR(400) NULL,
    CorrelationId          VARCHAR(64)   NULL,
    OccurredAt             DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX IX_Audit_Entity      ON audit.AuditLogs(EntityName, EntityId, OccurredAt DESC);
GO

CREATE INDEX IX_Audit_Institution ON audit.AuditLogs(InstitutionId, OccurredAt DESC);
GO
