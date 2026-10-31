/* A religious leader on an institution's roster (Phase E, V1). A person record: many leaders never
   log in, so UserId is optional and an administrator can answer for them. One institution each (V8). */
CREATE TABLE org.ReligiousLeaders (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    UserId        UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    DisplayName   NVARCHAR(100) NOT NULL,
    NameSi        NVARCHAR(100) NULL,
    NameTa        NVARCHAR(100) NULL,
    Title         NVARCHAR(40)  NULL,                -- free text: Ven., Rev., Fr., Moulavi, Kurukkal ...
    Phone         VARCHAR(20)   MASKED WITH (FUNCTION = 'partial(4, "XXXXX", 3)') NULL,
    Language      CHAR(2)       NOT NULL DEFAULT 'si'  -- texts to a leader without an account (V14)
        CONSTRAINT CK_ReligiousLeaders_Language CHECK (Language = 'en' OR Language = 'si' OR Language = 'ta'),
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy     UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    UpdatedAt     DATETIME2(3)  NULL,
    UpdatedBy     UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    RowVersion    ROWVERSION,
    PublicId      UNIQUEIDENTIFIER NOT NULL,         -- v7, set in C#; the leader's address in APIs
    CONSTRAINT UQ_ReligiousLeaders_Tenant UNIQUE (Id, InstitutionId)
);
GO

CREATE UNIQUE INDEX UX_ReligiousLeaders_PublicId ON org.ReligiousLeaders(PublicId);
GO

/* an account is one leader of an institution, at most */
CREATE UNIQUE INDEX UX_ReligiousLeaders_User ON org.ReligiousLeaders(InstitutionId, UserId) WHERE UserId IS NOT NULL;
GO
