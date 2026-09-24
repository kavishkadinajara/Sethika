/* Plans back the business model: Free / Plus / Partner. Limits are enforced by the app. */
CREATE TABLE org.Plans (
    Id               SMALLINT      IDENTITY(1,1) PRIMARY KEY,
    Code             VARCHAR(20)   NOT NULL UNIQUE,
    Name             NVARCHAR(50)  NOT NULL,
    MonthlyPriceLkr  DECIMAL(10,2) NOT NULL DEFAULT 0
        CONSTRAINT CK_Plans_MonthlyPriceLkr CHECK (MonthlyPriceLkr >= 0),
    MonthlySmsQuota  INT           NOT NULL DEFAULT 0,
    MaxStaffUsers    SMALLINT      NOT NULL DEFAULT 3,
    MaxMealSlots     SMALLINT      NOT NULL DEFAULT 5,
    IsPublic         BIT           NOT NULL DEFAULT 1,
    IsActive         BIT           NOT NULL DEFAULT 1
);
GO
