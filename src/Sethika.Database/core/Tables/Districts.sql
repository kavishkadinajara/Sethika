CREATE TABLE core.Districts (
    Id         SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    ProvinceId TINYINT       NOT NULL REFERENCES core.Provinces(Id),
    Code       VARCHAR(10)   NOT NULL UNIQUE,
    Name       NVARCHAR(100) NOT NULL,
    NameSi     NVARCHAR(100) NULL,
    NameTa     NVARCHAR(100) NULL
);
GO
