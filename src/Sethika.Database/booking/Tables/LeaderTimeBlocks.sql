/* A leader's committed time, one row per 15 minutes (Phase E, V2). The primary key is the
   guarantee that a leader is never in two places at once: an assignment or leave that overlaps
   anything already here fails with 2627, whatever the code or the Redis lock did. */
CREATE TABLE booking.LeaderTimeBlocks (
    LeaderId      INT          NOT NULL,
    BlockStart    DATETIME2(3) NOT NULL,             -- UTC, on the 15-minute grid
    InstitutionId INT          NOT NULL,
    Kind          VARCHAR(12)  NOT NULL
        CONSTRAINT CK_LeaderTimeBlocks_Kind CHECK (Kind = 'Appointment' OR Kind = 'TimeOff'),
    AppointmentId BIGINT       NULL,
    TimeOffId     INT          NULL,
    CONSTRAINT PK_LeaderTimeBlocks PRIMARY KEY (LeaderId, BlockStart),
    CONSTRAINT FK_LeaderTimeBlocks_Leader     FOREIGN KEY (LeaderId, InstitutionId)                REFERENCES org.ReligiousLeaders(Id, InstitutionId),
    CONSTRAINT FK_LeaderTimeBlocks_Assignment FOREIGN KEY (AppointmentId, LeaderId, InstitutionId) REFERENCES booking.AppointmentLeaders(AppointmentId, LeaderId, InstitutionId),
    CONSTRAINT FK_LeaderTimeBlocks_TimeOff    FOREIGN KEY (TimeOffId, LeaderId, InstitutionId)     REFERENCES org.LeaderTimeOff(Id, LeaderId, InstitutionId),
    CONSTRAINT CK_LeaderTimeBlocks_Source CHECK ((Kind = 'Appointment' AND AppointmentId IS NOT NULL AND TimeOffId IS NULL)
                                              OR (Kind = 'TimeOff' AND TimeOffId IS NOT NULL AND AppointmentId IS NULL)),
    CONSTRAINT CK_LeaderTimeBlocks_Grid CHECK (DATEPART(MINUTE, BlockStart) % 15 = 0 AND DATEPART(SECOND, BlockStart) = 0 AND DATEPART(MILLISECOND, BlockStart) = 0)
);
GO

CREATE INDEX IX_LeaderTimeBlocks_Appointment ON booking.LeaderTimeBlocks(AppointmentId) WHERE AppointmentId IS NOT NULL;
GO

CREATE INDEX IX_LeaderTimeBlocks_TimeOff ON booking.LeaderTimeBlocks(TimeOffId) WHERE TimeOffId IS NOT NULL;
GO
