CREATE TABLE core.LookupValues (
    Id           INT           IDENTITY(1,1) PRIMARY KEY,
    LookupTypeId SMALLINT      NOT NULL REFERENCES core.LookupTypes(Id),
    ParentId     INT           NULL REFERENCES core.LookupValues(Id),
    Code         VARCHAR(50)   NOT NULL,
    Name         NVARCHAR(150) NOT NULL,
    NameSi       NVARCHAR(150) NULL,
    NameTa       NVARCHAR(150) NULL,
    SortOrder    SMALLINT      NOT NULL DEFAULT 0,
    IsActive     BIT           NOT NULL DEFAULT 1,
    CONSTRAINT UQ_LookupValues_Type_Code UNIQUE (LookupTypeId, Code)
);
GO
