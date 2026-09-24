CREATE TABLE auth.ScreenElements (
    Id              INT           IDENTITY(1,1) PRIMARY KEY,
    ScreenId        SMALLINT      NOT NULL REFERENCES auth.Screens(Id),
    ParentElementId INT           NULL REFERENCES auth.ScreenElements(Id),
    Code            VARCHAR(120)  NOT NULL UNIQUE,
    Name            NVARCHAR(100) NOT NULL,
    ElementType     VARCHAR(20)   NOT NULL
        CONSTRAINT CK_ScreenElements_ElementType CHECK (ElementType = 'Button' OR ElementType = 'Dropdown' OR ElementType = 'DropdownOption' OR ElementType = 'Field' OR ElementType = 'Tab' OR ElementType = 'Section' OR ElementType = 'GridColumn' OR ElementType = 'ExportAction'),
    SortOrder       SMALLINT      NOT NULL DEFAULT 0,
    IsActive        BIT           NOT NULL DEFAULT 1
);
GO
