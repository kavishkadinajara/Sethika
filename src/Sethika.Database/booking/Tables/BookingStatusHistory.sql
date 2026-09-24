CREATE TABLE booking.BookingStatusHistory (
    Id         BIGINT       IDENTITY(1,1) PRIMARY KEY,
    BookingId  BIGINT       NOT NULL REFERENCES booking.Bookings(Id),
    FromStatus VARCHAR(20)  NULL,
    ToStatus   VARCHAR(20)  NOT NULL,
    ChangedBy  UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    ChangedAt  DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Comment    NVARCHAR(500) NULL
);
GO
