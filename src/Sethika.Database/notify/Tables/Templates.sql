CREATE TABLE notify.Templates (
    Id       SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    Code     VARCHAR(60)   NOT NULL,
    Channel  VARCHAR(10)   NOT NULL
        CONSTRAINT CK_Templates_Channel CHECK (Channel = 'SMS' OR Channel = 'Email' OR Channel = 'InApp' OR Channel = 'Push'),
    Language CHAR(2)       NOT NULL
        CONSTRAINT CK_Templates_Language CHECK (Language = 'en' OR Language = 'si' OR Language = 'ta'),
    Subject  NVARCHAR(200) NULL,
    Body     NVARCHAR(MAX) NOT NULL,
    IsActive BIT           NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Templates UNIQUE (Code, Channel, Language)
);
GO
