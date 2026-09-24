CREATE TABLE org.VerificationChecklistItems (
    Id                SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    InstitutionTypeId SMALLINT      NULL REFERENCES org.InstitutionTypes(Id),
    Code              VARCHAR(50)   NOT NULL UNIQUE,
    Question          NVARCHAR(300) NOT NULL,
    IsMandatory       BIT           NOT NULL DEFAULT 1,
    SortOrder         SMALLINT      NOT NULL DEFAULT 0,
    IsActive          BIT           NOT NULL DEFAULT 1
);
GO
