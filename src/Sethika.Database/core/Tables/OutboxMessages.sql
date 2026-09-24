/* Transactional outbox: domain events are written in the SAME transaction as the data change,
   then a Hangfire worker publishes them (SMS, email, SignalR, Redis cache invalidation). */
CREATE TABLE core.OutboxMessages (
    Id            BIGINT        IDENTITY(1,1) PRIMARY KEY,
    EventType     VARCHAR(100)  NOT NULL,          -- BookingApproved, InstitutionApproved, RolePermissionsChanged
    AggregateType VARCHAR(50)   NOT NULL,
    AggregateId   VARCHAR(50)   NOT NULL,
    Payload       NVARCHAR(MAX) NOT NULL
        CONSTRAINT CK_Outbox_Payload CHECK (ISJSON(Payload) = 1),
    CorrelationId VARCHAR(64)   NULL,
    OccurredAt    DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedAt   DATETIME2(3)  NULL,
    Attempts      TINYINT       NOT NULL DEFAULT 0,
    LastError     NVARCHAR(1000) NULL
);
GO

CREATE INDEX IX_Outbox_Pending ON core.OutboxMessages(OccurredAt) WHERE ProcessedAt IS NULL;
GO
