/*  Post-deployment: reference data. Every script here MUST be idempotent (MERGE / IF NOT EXISTS),
    because it runs on every publish. Business/test data does NOT belong here.  */
:r .\Seed\01_Portals.sql
:r .\Seed\02_PermissionActions.sql
:r .\Seed\03_LookupTypes.sql
:r .\Seed\04_ProvincesDistricts.sql
:r .\Seed\05_InstitutionTypes.sql
:r .\Seed\06_Plans.sql
/* TODO (confirm with real institutions before seeding):
   - org.DocumentTypes + org.InstitutionTypeRequiredDocuments
   - core.MemorialRules (per religion)
   - core.SpecialDays (Poya / holidays per year)
   - auth.Modules / Screens / ScreenElements / Permissions (grow with each screen that is built) */
