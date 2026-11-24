/* Institution's own donor book (CRM). Links phone/walk-in donors and online users into one history.
   Plan 08 (K1) brings it to life: staff add the people who give regularly, many of whom never use the
   app. UserId links a person whose phone is a Sethika account's confirmed phone. */
CREATE TABLE org.InstitutionDonors (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    UserId        UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    FullName      NVARCHAR(150) NOT NULL,
    Phone         VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NOT NULL,
    Email         NVARCHAR(256) NULL,
    Address       NVARCHAR(300) NULL,
    Notes         NVARCHAR(500) NULL,
    FirstGivenAt  DATE          NULL,
    LastGivenAt   DATE          NULL,
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    -- Added by plan 08 (K1) at the end of the table, so the deploy is an ALTER, never a rebuild of a
    -- table under RLS. The table was empty everywhere when they were added.
    PublicId      UNIQUEIDENTIFIER NOT NULL,         -- v7, set in C#; the donor's address in APIs
    Language      CHAR(2)       NOT NULL CONSTRAINT DF_InstDonor_Language DEFAULT 'si'  -- of their texts (K10)
        CONSTRAINT CK_InstDonor_Language CHECK (Language = 'en' OR Language = 'si' OR Language = 'ta'),
    -- "This person agreed to receive texts" (Q2): who ticked it and when. Unticked, a donor without an
    -- app account is never texted.
    TextConsentAt DATETIME2(3)  NULL,
    TextConsentBy UNIQUEIDENTIFIER NULL CONSTRAINT FK_InstDonor_TextConsentBy REFERENCES auth.Users(Id),
    CreatedBy     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_InstDonor_CreatedBy REFERENCES auth.Users(Id),
    UpdatedAt     DATETIME2(3)  NULL,
    UpdatedBy     UNIQUEIDENTIFIER NULL CONSTRAINT FK_InstDonor_UpdatedBy REFERENCES auth.Users(Id),
    RowVersion    ROWVERSION,
    CONSTRAINT UQ_InstDonor_Phone UNIQUE (InstitutionId, Phone),
    CONSTRAINT UQ_InstDonor_Tenant UNIQUE (Id, InstitutionId),         -- target for tenant-consistent FKs
    CONSTRAINT CK_InstDonor_TextConsent CHECK ((TextConsentAt IS NULL AND TextConsentBy IS NULL) OR (TextConsentAt IS NOT NULL AND TextConsentBy IS NOT NULL))
);
GO

CREATE UNIQUE INDEX UX_InstDonor_PublicId ON org.InstitutionDonors(PublicId);
GO

/* an account is one donor of an institution, at most */
CREATE UNIQUE INDEX UX_InstDonor_User ON org.InstitutionDonors(InstitutionId, UserId) WHERE UserId IS NOT NULL;
GO
