CREATE TABLE auth.RefreshTokens (
    Id                  BIGINT        IDENTITY(1,1) PRIMARY KEY,
    UserId              UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    TokenHash           CHAR(64)      NOT NULL UNIQUE,
    FamilyId            UNIQUEIDENTIFIER NOT NULL,   -- reuse of a rotated token revokes the whole family
    JwtId               VARCHAR(64)   NOT NULL,
    ActiveInstitutionId INT           NULL,
    DeviceInfo          NVARCHAR(200) NULL,
    CreatedByIp         VARCHAR(45)   NULL,
    CreatedAt           DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt           DATETIME2(3)  NOT NULL,
    RevokedAt           DATETIME2(3)  NULL,
    RevokedReason       NVARCHAR(100) NULL,
    ReplacedByTokenId   BIGINT        NULL REFERENCES auth.RefreshTokens(Id),
    -- The user's SecurityStamp when this token was issued. Refresh refuses a token whose stamp no
    -- longer matches, so rotating the stamp (password change, log out everywhere) ends every session
    -- issued before it - including one rotated by a refresh racing the rotation.
    SecurityStamp       NVARCHAR(100) NOT NULL,
    CONSTRAINT FK_RT_Institution FOREIGN KEY (ActiveInstitutionId) REFERENCES org.Institutions(Id)
);
GO

CREATE INDEX IX_RefreshTokens_User   ON auth.RefreshTokens(UserId) WHERE RevokedAt IS NULL;
GO

CREATE INDEX IX_RefreshTokens_Family ON auth.RefreshTokens(FamilyId);
GO
