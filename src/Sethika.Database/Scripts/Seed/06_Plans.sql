MERGE org.Plans AS t
USING (VALUES
 ('FREE',N'Free',0,100,3,5),
 ('PLUS',N'Plus',1500,1000,10,20),
 ('PARTNER',N'CSR Partner',0,3000,25,50)) AS s(Code,Name,MonthlyPriceLkr,MonthlySmsQuota,MaxStaffUsers,MaxMealSlots)
ON t.Code = s.Code
WHEN NOT MATCHED THEN INSERT (Code,Name,MonthlyPriceLkr,MonthlySmsQuota,MaxStaffUsers,MaxMealSlots)
     VALUES (s.Code,s.Name,s.MonthlyPriceLkr,s.MonthlySmsQuota,s.MaxStaffUsers,s.MaxMealSlots);
