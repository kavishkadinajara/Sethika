/* Trust & safety: anyone can report a suspicious institution */
CREATE TABLE org.InstitutionReports (
    Id              INT            IDENTITY(1,1) PRIMARY KEY,
    InstitutionId   INT            NOT NULL REFERENCES org.Institutions(Id),
    ReporterUserId  UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    ReasonLookupId  INT            NOT NULL REFERENCES core.LookupValues(Id),
    Details         NVARCHAR(2000) NOT NULL,
    EvidenceFileId  BIGINT         NULL REFERENCES core.Files(Id),
    Status          VARCHAR(15)    NOT NULL DEFAULT 'Open'
        CONSTRAINT CK_InstReport_Status CHECK (Status = 'Open' OR Status = 'Investigating' OR Status = 'Resolved' OR Status = 'Dismissed'),
    HandledBy       UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    HandledAt       DATETIME2(3)   NULL,
    Resolution      NVARCHAR(1000) NULL,
    CreatedAt       DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
