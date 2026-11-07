CREATE TABLE notify.Notifications (
    Id                BIGINT        IDENTITY(1,1) PRIMARY KEY,
    UserId            UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    InstitutionId     INT           NULL REFERENCES org.Institutions(Id),   -- SMS usage is billed against the plan quota
    TemplateId        SMALLINT      NULL REFERENCES notify.Templates(Id),
    Channel           VARCHAR(10)   NOT NULL
        CONSTRAINT CK_Notifications_Channel CHECK (Channel = 'SMS' OR Channel = 'Email' OR Channel = 'InApp' OR Channel = 'Push'),
    Recipient         NVARCHAR(256) NOT NULL,
    Subject           NVARCHAR(200) NULL,
    Body              NVARCHAR(MAX) NOT NULL,
    RelatedEntity     VARCHAR(50)   NULL,
    RelatedEntityId   VARCHAR(50)   NULL,
    DedupKey          VARCHAR(120)  NULL,     -- e.g. REMINDER_2D:BK-2026-000451 => never sent twice
    Status            VARCHAR(15)   NOT NULL DEFAULT 'Queued'
        CONSTRAINT CK_Notifications_Status CHECK (Status = 'Queued' OR Status = 'Sent' OR Status = 'Delivered' OR Status = 'Failed' OR Status = 'Cancelled'),
    ScheduledAt       DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    SentAt            DATETIME2(3)  NULL,
    ReadAt            DATETIME2(3)  NULL,
    Attempts          TINYINT       NOT NULL DEFAULT 0,
    SmsSegments       TINYINT       NULL,
    CostLkr           DECIMAL(8,2)  NULL,
    ProviderMessageId VARCHAR(100)  NULL,
    ErrorMessage      NVARCHAR(500) NULL
);
GO

CREATE UNIQUE INDEX UX_Notifications_Dedup ON notify.Notifications(DedupKey) WHERE DedupKey IS NOT NULL;
GO

CREATE INDEX IX_Notifications_Queue ON notify.Notifications(Status, ScheduledAt) WHERE Status = 'Queued';
GO

CREATE INDEX IX_Notifications_User  ON notify.Notifications(UserId, ScheduledAt DESC);
GO

CREATE INDEX IX_Notifications_PushReceipts ON notify.Notifications(SentAt) WHERE Channel = 'Push' AND Status = 'Sent';
GO
