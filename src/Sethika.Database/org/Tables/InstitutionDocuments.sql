CREATE TABLE org.InstitutionDocuments (
    Id                      INT          IDENTITY(1,1) PRIMARY KEY,
    InstitutionId           INT          NOT NULL REFERENCES org.Institutions(Id),
    DocumentTypeId          SMALLINT     NOT NULL REFERENCES org.DocumentTypes(Id),
    FileId                  BIGINT       NOT NULL REFERENCES core.Files(Id),
    SourceRequestDocumentId INT          NULL REFERENCES org.RegistrationRequestDocuments(Id),
    ReferenceNo             VARCHAR(80)  NULL,
    IssuedDate              DATE         NULL,
    ExpiryDate              DATE         NULL,
    Status                  VARCHAR(20)  NOT NULL DEFAULT 'Valid'
        CONSTRAINT CK_InstDoc_Status CHECK (Status = 'Valid' OR Status = 'Expired' OR Status = 'Replaced' OR Status = 'Revoked'),
    CreatedAt               DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE UNIQUE INDEX UX_InstDoc_Valid ON org.InstitutionDocuments(InstitutionId, DocumentTypeId) WHERE Status = 'Valid';
GO

CREATE INDEX IX_InstDoc_Expiry ON org.InstitutionDocuments(ExpiryDate) WHERE Status = 'Valid' AND ExpiryDate IS NOT NULL;
GO
