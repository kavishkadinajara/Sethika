/* Who is assigned to an appointment, and their answer (Phase E, promise 4). "Needs leaders" is
   computed from these rows, never stored (V6). */
CREATE TABLE booking.AppointmentLeaders (
    AppointmentId     BIGINT        NOT NULL,
    LeaderId          INT           NOT NULL,
    InstitutionId     INT           NOT NULL,
    Response          VARCHAR(10)   NOT NULL DEFAULT 'Pending'
        CONSTRAINT CK_AppointmentLeaders_Response CHECK (Response = 'Pending' OR Response = 'Accepted' OR Response = 'Declined' OR Response = 'Removed'),
    AssignedBy        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    AssignedAt        DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    RespondedBy       UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    RespondedAt       DATETIME2(3)  NULL,
    RespondedOnBehalf BIT           NOT NULL DEFAULT 0,
    DeclineReason     NVARCHAR(300) NULL,
    CONSTRAINT PK_AppointmentLeaders PRIMARY KEY (AppointmentId, LeaderId),
    CONSTRAINT FK_AppointmentLeaders_Appointment FOREIGN KEY (AppointmentId, InstitutionId) REFERENCES booking.ServiceAppointments(Id, InstitutionId),
    CONSTRAINT FK_AppointmentLeaders_Leader      FOREIGN KEY (LeaderId, InstitutionId)      REFERENCES org.ReligiousLeaders(Id, InstitutionId),
    CONSTRAINT UQ_AppointmentLeaders_Tenant UNIQUE (AppointmentId, LeaderId, InstitutionId)
);
GO

/* a leader's schedule */
CREATE INDEX IX_AppointmentLeaders_Leader ON booking.AppointmentLeaders(LeaderId, Response);
GO
