CREATE TABLE core.Files (
    Id              BIGINT        IDENTITY(1,1) PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() UNIQUE,
    StorageProvider VARCHAR(20)   NOT NULL
        CONSTRAINT CK_Files_StorageProvider CHECK (StorageProvider = 'Cloudinary' OR StorageProvider = 'AzureBlob' OR StorageProvider = 'Local'),
    StorageKey      NVARCHAR(400) NOT NULL,
    OriginalName    NVARCHAR(255) NOT NULL,
    ContentType     VARCHAR(100)  NOT NULL,
    SizeBytes       BIGINT        NOT NULL
        CONSTRAINT CK_Files_SizeBytes CHECK (SizeBytes > 0),
    Sha256Hash      CHAR(64)      NOT NULL,
    IsPrivate       BIT           NOT NULL DEFAULT 1,
    ScanStatus      VARCHAR(15)   NOT NULL DEFAULT 'Pending'
        CONSTRAINT CK_Files_ScanStatus CHECK (ScanStatus = 'Pending' OR ScanStatus = 'Clean' OR ScanStatus = 'Infected' OR ScanStatus = 'Failed'),  -- virus scan before admins open it
    UploadedBy      UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    UploadedAt      DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted       BIT           NOT NULL DEFAULT 0
);
GO

CREATE INDEX IX_Files_Hash ON core.Files(Sha256Hash);   -- detect the same document reused across requests
GO
