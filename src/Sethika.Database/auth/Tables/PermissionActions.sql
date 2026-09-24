CREATE TABLE auth.PermissionActions (
    Id        TINYINT      PRIMARY KEY,
    Code      VARCHAR(20)  NOT NULL UNIQUE,
    Name      NVARCHAR(50) NOT NULL,
    SortOrder TINYINT      NOT NULL DEFAULT 0
);
GO
