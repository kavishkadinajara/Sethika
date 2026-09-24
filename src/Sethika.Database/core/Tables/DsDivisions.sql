CREATE TABLE core.DsDivisions (
    Id         SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    DistrictId SMALLINT      NOT NULL REFERENCES core.Districts(Id),
    Name       NVARCHAR(100) NOT NULL,
    NameSi     NVARCHAR(100) NULL,
    NameTa     NVARCHAR(100) NULL,
    CONSTRAINT UQ_DsDivisions_District_Name UNIQUE (DistrictId, Name)
);
GO
