MERGE core.Provinces AS t
USING (VALUES ('WP',N'Western'),('CP',N'Central'),('SP',N'Southern'),('NP',N'Northern'),('EP',N'Eastern'),
              ('NWP',N'North Western'),('NCP',N'North Central'),('UP',N'Uva'),('SGP',N'Sabaragamuwa')) AS s(Code,Name)
ON t.Code = s.Code
WHEN MATCHED THEN UPDATE SET Name = s.Name
WHEN NOT MATCHED THEN INSERT (Code,Name) VALUES (s.Code,s.Name);

MERGE core.Districts AS t
USING (
    SELECT d.Code, d.Name, p.Id AS ProvinceId
    FROM (VALUES
      ('CMB',N'Colombo','WP'),('GMP',N'Gampaha','WP'),('KLT',N'Kalutara','WP'),
      ('KDY',N'Kandy','CP'),('MTL',N'Matale','CP'),('NEL',N'Nuwara Eliya','CP'),
      ('GLL',N'Galle','SP'),('MTR',N'Matara','SP'),('HBT',N'Hambantota','SP'),
      ('JAF',N'Jaffna','NP'),('KLN',N'Kilinochchi','NP'),('MNR',N'Mannar','NP'),('VAV',N'Vavuniya','NP'),('MLT',N'Mullaitivu','NP'),
      ('BTC',N'Batticaloa','EP'),('AMP',N'Ampara','EP'),('TRC',N'Trincomalee','EP'),
      ('KRN',N'Kurunegala','NWP'),('PTM',N'Puttalam','NWP'),
      ('ANP',N'Anuradhapura','NCP'),('PLN',N'Polonnaruwa','NCP'),
      ('BDL',N'Badulla','UP'),('MNG',N'Monaragala','UP'),
      ('RTP',N'Ratnapura','SGP'),('KGL',N'Kegalle','SGP')) AS d(Code,Name,ProvinceCode)
    JOIN core.Provinces p ON p.Code = d.ProvinceCode
) AS s
ON t.Code = s.Code
WHEN MATCHED THEN UPDATE SET Name = s.Name, ProvinceId = s.ProvinceId
WHEN NOT MATCHED THEN INSERT (Code,Name,ProvinceId) VALUES (s.Code,s.Name,s.ProvinceId);
