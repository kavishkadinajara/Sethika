/* A donor's request for a religious service (Phase E, promise 2). Times are UTC on the 15-minute
   grid (V7). A request holds no leader: blocks are written only when leaders are assigned. */
CREATE TABLE booking.ServiceAppointments (
    Id            BIGINT        IDENTITY(1,1) PRIMARY KEY,
    AppointmentNo VARCHAR(20)   NOT NULL UNIQUE,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    ServiceTypeId INT           NOT NULL,
    DonorUserId   UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    DonorName     NVARCHAR(150) NOT NULL,            -- snapshot
    DonorPhone    VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NOT NULL,
    StartsAt      DATETIME2(3)  NOT NULL,
    EndsAt        DATETIME2(3)  NOT NULL,
    LocationMode  VARCHAR(15)   NOT NULL
        CONSTRAINT CK_ServiceAppointments_LocationMode CHECK (LocationMode = 'AtInstitution' OR LocationMode = 'AtDonorPlace'),
    Address       NVARCHAR(300) NULL,
    Occasion      NVARCHAR(200) NULL,
    DonorNote     NVARCHAR(500) NULL,
    Status        VARCHAR(15)   NOT NULL DEFAULT 'Requested'
        CONSTRAINT CK_ServiceAppointments_Status CHECK (Status = 'Requested' OR Status = 'Approved' OR Status = 'Rejected' OR Status = 'Cancelled' OR Status = 'Completed'),
    CreatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    DecidedBy     UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    DecidedAt     DATETIME2(3)  NULL,
    RejectReason  NVARCHAR(300) NULL,
    CancelledBy   UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    CancelledAt   DATETIME2(3)  NULL,
    CancelReason  NVARCHAR(300) NULL,
    CompletedAt   DATETIME2(3)  NULL,
    RowVersion    ROWVERSION,
    CONSTRAINT FK_ServiceAppointments_Type FOREIGN KEY (ServiceTypeId, InstitutionId) REFERENCES booking.ServiceTypes(Id, InstitutionId),
    CONSTRAINT UQ_ServiceAppointments_Tenant UNIQUE (Id, InstitutionId),
    CONSTRAINT CK_ServiceAppointments_Range   CHECK (EndsAt > StartsAt),
    CONSTRAINT CK_ServiceAppointments_Grid    CHECK (DATEPART(MINUTE, StartsAt) % 15 = 0 AND DATEPART(SECOND, StartsAt) = 0 AND DATEPART(MILLISECOND, StartsAt) = 0
                                               AND DATEPART(MINUTE, EndsAt) % 15 = 0 AND DATEPART(SECOND, EndsAt) = 0 AND DATEPART(MILLISECOND, EndsAt) = 0),
    CONSTRAINT CK_ServiceAppointments_Address CHECK (LocationMode <> 'AtDonorPlace' OR Address IS NOT NULL),
    CONSTRAINT CK_ServiceAppointments_Reject  CHECK (Status <> 'Rejected'  OR RejectReason IS NOT NULL),
    CONSTRAINT CK_ServiceAppointments_Cancel  CHECK (Status <> 'Cancelled' OR CancelledAt IS NOT NULL)
);
GO

CREATE INDEX IX_ServiceAppointments_Institution ON booking.ServiceAppointments(InstitutionId, StartsAt);
GO

CREATE INDEX IX_ServiceAppointments_Donor ON booking.ServiceAppointments(DonorUserId, StartsAt DESC);
GO

/* the reminder job: tomorrow's approved appointments */
CREATE INDEX IX_ServiceAppointments_Status ON booking.ServiceAppointments(Status, StartsAt);
GO
