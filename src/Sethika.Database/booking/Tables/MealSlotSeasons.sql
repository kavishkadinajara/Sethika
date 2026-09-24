/* Seasonal windows: iftar slot only during Ramadan, special annadanam during a festival.
   If a slot has seasons, it is bookable ONLY inside them. */
CREATE TABLE booking.MealSlotSeasons (
    Id         INT           IDENTITY(1,1) PRIMARY KEY,
    MealSlotId INT           NOT NULL REFERENCES booking.MealSlots(Id) ON DELETE CASCADE,
    Name       NVARCHAR(100) NOT NULL,          -- "Ramadan 2027"
    DateFrom   DATE          NOT NULL,
    DateTo     DATE          NOT NULL,
    CONSTRAINT CK_Seasons_Range CHECK (DateTo >= DateFrom)
);
GO
