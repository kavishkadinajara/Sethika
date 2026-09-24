CREATE TABLE notify.UserPreferences (
    UserId    UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    EventCode VARCHAR(60)  NOT NULL,
    Channel   VARCHAR(10)  NOT NULL
        CONSTRAINT CK_NotifyPref_Channel CHECK (Channel = 'SMS' OR Channel = 'Email' OR Channel = 'InApp' OR Channel = 'Push'),
    IsEnabled BIT          NOT NULL DEFAULT 1,
    PRIMARY KEY (UserId, EventCode, Channel)
);
GO
