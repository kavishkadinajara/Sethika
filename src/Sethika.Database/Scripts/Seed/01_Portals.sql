MERGE auth.Portals AS t
USING (VALUES (1,'ADMIN',N'Maintenance Admin'),(2,'INSTITUTION',N'Institution Dashboard'),(3,'DONOR',N'Donor Portal')) AS s(Id,Code,Name)
ON t.Id = s.Id
WHEN MATCHED AND (t.Code <> s.Code OR t.Name <> s.Name) THEN UPDATE SET Code = s.Code, Name = s.Name
WHEN NOT MATCHED THEN INSERT (Id,Code,Name) VALUES (s.Id,s.Code,s.Name);
