/* Versioned legal texts: Terms, Privacy Policy, Institution Declaration */
CREATE TABLE core.LegalDocuments (
    Id            SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    Code          VARCHAR(40)   NOT NULL,          -- TERMS, PRIVACY, INSTITUTION_DECLARATION
    Version       VARCHAR(10)   NOT NULL,
    Language      CHAR(2)       NOT NULL
        CONSTRAINT CK_LegalDoc_Language CHECK (Language = 'en' OR Language = 'si' OR Language = 'ta'),
    Title         NVARCHAR(200) NOT NULL,
    Content       NVARCHAR(MAX) NOT NULL,
    ContentSha256 CHAR(64)      NOT NULL,          -- proves exactly what text was accepted
    EffectiveFrom DATETIME2(3)  NOT NULL,
    IsCurrent     BIT           NOT NULL DEFAULT 0,
    CONSTRAINT UQ_LegalDocuments UNIQUE (Code, Version, Language)
);
GO

CREATE UNIQUE INDEX UX_LegalDocuments_Current ON core.LegalDocuments(Code, Language) WHERE IsCurrent = 1;
GO
