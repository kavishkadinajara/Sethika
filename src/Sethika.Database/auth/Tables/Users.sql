CREATE TABLE auth.Users (
    Id                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    UserType           VARCHAR(20)   NOT NULL
        CONSTRAINT CK_Users_UserType CHECK (UserType = 'PlatformStaff' OR UserType = 'InstitutionStaff' OR UserType = 'Donor'),
    FullName           NVARCHAR(150) NOT NULL,
    DisplayName        NVARCHAR(80)  NULL,
    Email              NVARCHAR(256) MASKED WITH (FUNCTION = 'email()') NULL,
    NormalizedEmail    NVARCHAR(256) NULL,
    EmailConfirmed     BIT           NOT NULL DEFAULT 0,
    PhoneNumber        VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NULL,
    PhoneConfirmed     BIT           NOT NULL DEFAULT 0,
    NicNumber          VARCHAR(12)   MASKED WITH (FUNCTION = 'partial(2, "XXXXXXX", 2)') NULL,
    PasswordHash       NVARCHAR(MAX) NULL,
    PasswordChangedAt  DATETIME2(3)  NULL,
    SecurityStamp      NVARCHAR(100) NOT NULL DEFAULT CONVERT(NVARCHAR(100), NEWID()),
    PermissionsVersion INT           NOT NULL DEFAULT 1,     -- bump => Redis permission cache invalidated
    TwoFactorEnabled   BIT           NOT NULL DEFAULT 0,
    MfaRequired        BIT           NOT NULL DEFAULT 0,     -- policy: forced for platform staff & institution owners
    LockoutEnd         DATETIMEOFFSET NULL,
    AccessFailedCount  INT           NOT NULL DEFAULT 0,
    PreferredLanguage  CHAR(2)       NOT NULL DEFAULT 'en'
        CONSTRAINT CK_Users_PreferredLanguage CHECK (PreferredLanguage = 'en' OR PreferredLanguage = 'si' OR PreferredLanguage = 'ta'),
    ProfilePhotoFileId BIGINT        NULL,
    Status             VARCHAR(20)   NOT NULL DEFAULT 'Active'
        CONSTRAINT CK_Users_Status CHECK (Status = 'PendingVerification' OR Status = 'Active' OR Status = 'Suspended' OR Status = 'Deactivated'),
    LastLoginAt        DATETIME2(3)  NULL,
    CreatedAt          DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy          UNIQUEIDENTIFIER NULL,
    UpdatedAt          DATETIME2(3)  NULL,
    UpdatedBy          UNIQUEIDENTIFIER NULL,
    IsDeleted          BIT           NOT NULL DEFAULT 0,
    DeletedAt          DATETIME2(3)  NULL,
    RowVersion         ROWVERSION,
    CONSTRAINT CK_Users_EmailOrPhone CHECK (Email IS NOT NULL OR PhoneNumber IS NOT NULL),
    CONSTRAINT FK_Users_ProfilePhoto FOREIGN KEY (ProfilePhotoFileId) REFERENCES core.Files(Id)
);
GO

CREATE UNIQUE INDEX UX_Users_NormalizedEmail ON auth.Users(NormalizedEmail) WHERE NormalizedEmail IS NOT NULL AND IsDeleted = 0;
GO

CREATE UNIQUE INDEX UX_Users_Phone           ON auth.Users(PhoneNumber)     WHERE PhoneNumber IS NOT NULL AND IsDeleted = 0;
GO
