CREATE TABLE org.PlanModules (
    PlanId   SMALLINT NOT NULL REFERENCES org.Plans(Id),
    ModuleId SMALLINT NOT NULL REFERENCES auth.Modules(Id),
    PRIMARY KEY (PlanId, ModuleId)
);
GO
