CREATE TABLE org.InstitutionTypeDefaultModules (
    InstitutionTypeId SMALLINT NOT NULL REFERENCES org.InstitutionTypes(Id),
    ModuleId          SMALLINT NOT NULL REFERENCES auth.Modules(Id),
    PRIMARY KEY (InstitutionTypeId, ModuleId)
);
GO
