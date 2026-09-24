CREATE TABLE core.SystemSettings (
    SettingKey   VARCHAR(100)  PRIMARY KEY,
    SettingValue NVARCHAR(MAX) NOT NULL,
    DataType     VARCHAR(20)   NOT NULL
        CONSTRAINT CK_SysSetting_DataType CHECK (DataType = 'String' OR DataType = 'Int' OR DataType = 'Decimal' OR DataType = 'Bool' OR DataType = 'Json'),
    Description  NVARCHAR(300) NULL,
    IsSecret     BIT           NOT NULL DEFAULT 0,
    UpdatedAt    DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedBy    UNIQUEIDENTIFIER NULL
);
GO
