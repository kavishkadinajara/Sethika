CREATE TABLE org.RegistrationRequestChecklist (
    RequestId       INT      NOT NULL REFERENCES org.RegistrationRequests(Id) ON DELETE CASCADE,
    ChecklistItemId SMALLINT NOT NULL REFERENCES org.VerificationChecklistItems(Id),
    IsPassed        BIT      NOT NULL,
    CheckedBy       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CheckedAt       DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    EvidenceFileId  BIGINT   NULL REFERENCES core.Files(Id),    -- e.g. site-visit photo
    Remarks         NVARCHAR(500) NULL,
    PRIMARY KEY (RequestId, ChecklistItemId)
);
GO
