CREATE TABLE auth.Modules (
    Id        SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    PortalId  TINYINT       NOT NULL REFERENCES auth.Portals(Id),
    Code      VARCHAR(50)   NOT NULL UNIQUE,
    Name      NVARCHAR(100) NOT NULL,
    Icon      VARCHAR(50)   NULL,
    SortOrder SMALLINT      NOT NULL DEFAULT 0,
    IsCore    BIT           NOT NULL DEFAULT 0,
    IsActive  BIT           NOT NULL DEFAULT 1
);
GO
