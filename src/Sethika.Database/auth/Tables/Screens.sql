CREATE TABLE auth.Screens (
    Id             SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    ModuleId       SMALLINT      NOT NULL REFERENCES auth.Modules(Id),
    ParentScreenId SMALLINT      NULL REFERENCES auth.Screens(Id),
    Code           VARCHAR(80)   NOT NULL UNIQUE,
    Name           NVARCHAR(100) NOT NULL,
    Route          VARCHAR(200)  NULL,
    IsMenuItem     BIT           NOT NULL DEFAULT 1,
    SortOrder      SMALLINT      NOT NULL DEFAULT 0,
    IsActive       BIT           NOT NULL DEFAULT 1
);
GO
