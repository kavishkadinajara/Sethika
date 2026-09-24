MERGE org.InstitutionTypes AS t
USING (VALUES
 ('TEMPLE',N'Buddhist Temple','Religious',N'Dana'),
 ('KOVIL',N'Hindu Kovil','Religious',N'Annadanam'),
 ('MOSQUE',N'Mosque','Religious',N'Iftar / Meal'),
 ('CHURCH',N'Church','Religious',N'Meal'),
 ('ELDERS_HOME',N'Elders'' Home','CareHome',N'Meal'),
 ('CHILDRENS_HOME',N'Children''s Home','CareHome',N'Meal')) AS s(Code,Name,Category,MealSlotLabel)
ON t.Code = s.Code
WHEN MATCHED THEN UPDATE SET Name = s.Name, Category = s.Category, MealSlotLabel = s.MealSlotLabel
WHEN NOT MATCHED THEN INSERT (Code,Name,Category,MealSlotLabel) VALUES (s.Code,s.Name,s.Category,s.MealSlotLabel);
