/* A day let go from a standing sponsorship, or moved (plan 08, K4). The nightly job never books a
   day that has one, so a cancelled standing booking is not booked again the next night.
     Skipped    the sponsor cannot give that day (K5)
     Released   given to a donor who asked for it (K6): DayRequestId
     Blocked    the institution has another use for the day (K7)
     MovedTo    a swap (K8): this sponsor's day went to OtherDate
     MovedFrom  a swap: this sponsor gives on ExceptionDate instead of OtherDate
   BookingId is the booking the change cancelled, or for MovedFrom the one it made. */
CREATE TABLE booking.StandingExceptions (
    Id                    BIGINT        IDENTITY(1,1) PRIMARY KEY,
    InstitutionId         INT           NOT NULL CONSTRAINT FK_StandingExc_Institution REFERENCES org.Institutions(Id),
    StandingSponsorshipId INT           NOT NULL,
    ExceptionDate         DATE          NOT NULL,
    Kind                  VARCHAR(10)   NOT NULL
        CONSTRAINT CK_StandingExc_Kind CHECK (Kind = 'Skipped' OR Kind = 'Released' OR Kind = 'Blocked' OR Kind = 'MovedTo' OR Kind = 'MovedFrom'),
    OtherDate             DATE          NULL,
    BookingId             BIGINT        NULL,
    DayRequestId          INT           NULL,
    Reason                NVARCHAR(300) NULL,
    CreatedBy             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_StandingExc_CreatedBy REFERENCES auth.Users(Id),
    CreatedAt             DATETIME2(3)  NOT NULL CONSTRAINT DF_StandingExc_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_StandingExc_StandingTenant FOREIGN KEY (StandingSponsorshipId, InstitutionId) REFERENCES booking.StandingSponsorships(Id, InstitutionId),
    CONSTRAINT FK_StandingExc_BookingTenant  FOREIGN KEY (BookingId, InstitutionId)             REFERENCES booking.Bookings(Id, InstitutionId),
    CONSTRAINT FK_StandingExc_RequestTenant  FOREIGN KEY (DayRequestId, InstitutionId)          REFERENCES booking.DayRequests(Id, InstitutionId),
    -- One state per sponsorship and day: a second change to a day changes this row.
    CONSTRAINT UQ_StandingExc_Day UNIQUE (StandingSponsorshipId, ExceptionDate),
    -- A swap names its other day, which is another day; nothing else names one.
    CONSTRAINT CK_StandingExc_Swap CHECK (
        ((Kind = 'MovedTo' OR Kind = 'MovedFrom') AND OtherDate IS NOT NULL AND OtherDate <> ExceptionDate)
     OR (Kind <> 'MovedTo' AND Kind <> 'MovedFrom' AND OtherDate IS NULL)),
    -- A released day names the request it was given to; nothing else names one.
    CONSTRAINT CK_StandingExc_Request CHECK ((Kind = 'Released' AND DayRequestId IS NOT NULL) OR (Kind <> 'Released' AND DayRequestId IS NULL))
);
GO

/* "which days of these sponsorships are let go" for a date range: the job and the calendar */
CREATE INDEX IX_StandingExc_Date ON booking.StandingExceptions(ExceptionDate) INCLUDE (StandingSponsorshipId, Kind);
GO
