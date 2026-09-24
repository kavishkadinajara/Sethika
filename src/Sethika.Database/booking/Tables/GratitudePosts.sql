/* Gratitude wall: institution thanks the donor with a note & photos (donor consents before it goes public) */
CREATE TABLE booking.GratitudePosts (
    Id             BIGINT         IDENTITY(1,1) PRIMARY KEY,
    PublicId       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() UNIQUE,
    InstitutionId  INT            NOT NULL REFERENCES org.Institutions(Id),
    BookingId      BIGINT         NULL,
    DonationId     BIGINT         NULL,
    Message        NVARCHAR(1000) NOT NULL,
    Visibility     VARCHAR(10)    NOT NULL DEFAULT 'Private'
        CONSTRAINT CK_Gratitude_Visibility CHECK (Visibility = 'Private' OR Visibility = 'Public'),
    DonorConsentAt DATETIME2(3)   NULL,
    CreatedBy      UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    CreatedAt      DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Gratitude_BookingTenant FOREIGN KEY (BookingId, InstitutionId) REFERENCES booking.Bookings(Id, InstitutionId),
    CONSTRAINT CK_Gratitude_Target  CHECK (BookingId IS NOT NULL OR DonationId IS NOT NULL),
    CONSTRAINT CK_Gratitude_Consent CHECK (Visibility = 'Private' OR DonorConsentAt IS NOT NULL),
    CONSTRAINT FK_Gratitude_Donation FOREIGN KEY (DonationId) REFERENCES giving.Donations(Id)
);
GO
