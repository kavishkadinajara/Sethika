CREATE TABLE org.InstitutionTypeRequiredDocuments (
    InstitutionTypeId SMALLINT      NOT NULL REFERENCES org.InstitutionTypes(Id),
    DocumentTypeId    SMALLINT      NOT NULL REFERENCES org.DocumentTypes(Id),
    IsMandatory       BIT           NOT NULL DEFAULT 1,
    SortOrder         SMALLINT      NOT NULL DEFAULT 0,
    Instructions      NVARCHAR(400) NULL,
    PRIMARY KEY (InstitutionTypeId, DocumentTypeId)
);
GO
