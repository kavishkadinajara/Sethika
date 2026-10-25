/* Every status change of an appointment. No InstitutionId and no RLS, the same as
   BookingStatusHistory: the donor writes rows too (request, cancel), from a personal session. */
CREATE TABLE booking.AppointmentStatusHistory (
    Id            BIGINT       IDENTITY(1,1) PRIMARY KEY,
    AppointmentId BIGINT       NOT NULL REFERENCES booking.ServiceAppointments(Id),
    FromStatus    VARCHAR(15)  NULL,
    ToStatus      VARCHAR(15)  NOT NULL,
    ChangedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ChangedAt     DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Comment       NVARCHAR(500) NULL
);
GO
