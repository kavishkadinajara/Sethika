/* When a leader is normally available. Only a filter for the assignment picker: the time blocks
   are the guarantee (Phase E, V2). */
CREATE TABLE org.LeaderWeeklyHours (
    Id            INT          IDENTITY(1,1) PRIMARY KEY,
    LeaderId      INT          NOT NULL,
    InstitutionId INT          NOT NULL,
    DayOfWeek     TINYINT      NOT NULL               -- 0 Sunday ... 6 Saturday
        CONSTRAINT CK_LeaderWeeklyHours_Day CHECK (DayOfWeek <= 6),
    StartTime     TIME(0)      NOT NULL,
    EndTime       TIME(0)      NOT NULL,
    CONSTRAINT FK_LeaderWeeklyHours_Leader FOREIGN KEY (LeaderId, InstitutionId) REFERENCES org.ReligiousLeaders(Id, InstitutionId),
    CONSTRAINT CK_LeaderWeeklyHours_Time CHECK (EndTime > StartTime)
);
GO

CREATE INDEX IX_LeaderWeeklyHours_Leader ON org.LeaderWeeklyHours(LeaderId, DayOfWeek);
GO
