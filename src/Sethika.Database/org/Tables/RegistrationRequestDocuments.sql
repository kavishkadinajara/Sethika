CREATE TABLE org.RegistrationRequestDocuments (
    Id                 INT           IDENTITY(1,1) PRIMARY KEY,
    RequestId          INT           NOT NULL REFERENCES org.RegistrationRequests(Id) ON DELETE CASCADE,
    DocumentTypeId     SMALLINT      NOT NULL REFERENCES org.DocumentTypes(Id),
    FileId             BIGINT        NOT NULL REFERENCES core.Files(Id),
    ReferenceNo        VARCHAR(80)   NULL,
    IssuedBy           NVARCHAR(200) NULL,
    IssuedDate         DATE          NULL,
    ExpiryDate         DATE          NULL,
    VerificationStatus VARCHAR(20)   NOT NULL DEFAULT 'Pending'
        CONSTRAINT CK_ReqDoc_VerificationStatus CHECK (VerificationStatus = 'Pending' OR VerificationStatus = 'Verified' OR VerificationStatus = 'Rejected' OR VerificationStatus = 'Superseded'),
    VerifiedBy         UNIQUEIDENTIFIER NULL REFERENCES auth.Users(Id),
    VerifiedAt         DATETIME2(3)  NULL,
    ReviewerRemarks    NVARCHAR(500) NULL,
    UploadedAt         DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ReqDoc_Dates CHECK (ExpiryDate IS NULL OR IssuedDate IS NULL OR ExpiryDate > IssuedDate)
);
GO

CREATE UNIQUE INDEX UX_ReqDoc_Live ON org.RegistrationRequestDocuments(RequestId, DocumentTypeId)
    WHERE VerificationStatus <> 'Superseded';
GO
