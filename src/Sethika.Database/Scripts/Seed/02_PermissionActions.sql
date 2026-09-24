MERGE auth.PermissionActions AS t
USING (VALUES
 (1,'VIEW',N'View',1),(2,'CREATE',N'Create',2),(3,'EDIT',N'Edit',3),(4,'DELETE',N'Delete',4),
 (5,'APPROVE',N'Approve',5),(6,'REJECT',N'Reject',6),(7,'EXPORT',N'Export',7),(8,'PRINT',N'Print',8),
 (9,'ENABLE',N'Enable / use element',9),(10,'EXECUTE',N'Execute action',10)) AS s(Id,Code,Name,SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Code = s.Code, Name = s.Name, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id,Code,Name,SortOrder) VALUES (s.Id,s.Code,s.Name,s.SortOrder);
