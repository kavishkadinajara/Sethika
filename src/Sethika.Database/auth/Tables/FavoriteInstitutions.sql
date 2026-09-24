CREATE TABLE auth.FavoriteInstitutions (
    UserId        UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    InstitutionId INT              NOT NULL,
    AddedAt       DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (UserId, InstitutionId),
    CONSTRAINT FK_Fav_Institution FOREIGN KEY (InstitutionId) REFERENCES org.Institutions(Id)
);
GO
