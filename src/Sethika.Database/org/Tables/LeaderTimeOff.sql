/* A leader's leave. Each period writes TimeOff blocks in the same transaction, so leave and an
   assignment can never overlap (Phase E, V2). UTC, on the 15-minute grid. */
CREATE TABLE org.LeaderTimeOff (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    LeaderId      INT           NOT NULL,
    InstitutionId INT           NOT NULL,
    StartsAt      DATETIME2(3)  NOT NULL,
    EndsAt        DATETIME2(3)  NOT NULL,
    Reason        NVARCHAR(200) NULL,
    CreatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CONSTRAINT FK_LeaderTimeOff_Leader FOREIGN KEY (LeaderId, InstitutionId) REFERENCES org.ReligiousLeaders(Id, InstitutionId),
    CONSTRAINT UQ_LeaderTimeOff_Leader UNIQUE (Id, LeaderId, InstitutionId),
    CONSTRAINT CK_LeaderTimeOff_Range  CHECK (EndsAt > StartsAt),
    CONSTRAINT CK_LeaderTimeOff_Grid   CHECK (DATEPART(MINUTE, StartsAt) % 15 = 0 AND DATEPART(SECOND, StartsAt) = 0 AND DATEPART(MILLISECOND, StartsAt) = 0
                                        AND DATEPART(MINUTE, EndsAt) % 15 = 0 AND DATEPART(SECOND, EndsAt) = 0 AND DATEPART(MILLISECOND, EndsAt) = 0)
);
GO

CREATE INDEX IX_LeaderTimeOff_Leader ON org.LeaderTimeOff(LeaderId, StartsAt);
GO
