/* Lookup TYPES only. Values (occasions, units, religions ...) are added through the admin UI or later seeds. */
MERGE core.LookupTypes AS t
USING (VALUES
 ('OCCASION',N'Occasion'),('NEED_CATEGORY',N'Need category'),('UNIT',N'Unit of measure'),
 ('PAYMENT_METHOD',N'Payment method'),('RELIGION',N'Religion'),('RELATIONSHIP',N'Relationship'),
 ('REJECT_REASON',N'Registration reject reason'),('REPORT_REASON',N'Institution report reason')) AS s(Code,Name)
ON t.Code = s.Code
WHEN MATCHED THEN UPDATE SET Name = s.Name
WHEN NOT MATCHED THEN INSERT (Code,Name,IsSystem) VALUES (s.Code,s.Name,1);
