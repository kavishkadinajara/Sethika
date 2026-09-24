CREATE TABLE org.DocumentTypes (
    Id                  SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    Code                VARCHAR(50)   NOT NULL UNIQUE,
    Name                NVARCHAR(150) NOT NULL,
    Description         NVARCHAR(400) NULL,
    AllowedExtensions   VARCHAR(100)  NOT NULL DEFAULT '.pdf,.jpg,.jpeg,.png',
    MaxSizeMb           TINYINT       NOT NULL DEFAULT 5,
    RequiresReferenceNo BIT           NOT NULL DEFAULT 1,
    RequiresIssueDate   BIT           NOT NULL DEFAULT 1,
    RequiresExpiryDate  BIT           NOT NULL DEFAULT 0,
    IsActive            BIT           NOT NULL DEFAULT 1
);
GO
