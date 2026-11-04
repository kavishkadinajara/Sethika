/*  Giving's lists (plan 09, M20): how money was given, what a need is for, and what it is counted in.
    Sethika's own vocabulary, like the reject reasons, so not drafts: nothing here is a fact about a
    faith or an institution. The Sinhala and Tamil names are drafts for review
    (docs/translations-to-review.md, "Giving lists").

    PAYHERE is the online gateway's own method (M9): set by the notification, never chosen when staff
    record a gift at the counter.

    Matched on (type, Code), never on Id: donations and needs point at these rows. Values are only
    added or updated here; retiring one is IsActive = 0, because recorded gifts still name it.     */
MERGE core.LookupValues AS t
USING (
    SELECT lt.Id AS LookupTypeId, v.Code, v.Name, v.NameSi, v.NameTa, v.SortOrder
    FROM (VALUES
        ('PAYMENT_METHOD', 'CASH',             N'Cash',                   N'මුදල්',                  N'ரொக்கம்',                    10),
        ('PAYMENT_METHOD', 'BANK_TRANSFER',    N'Bank transfer',          N'බැංකු මාරුව',             N'வங்கிப் பரிமாற்றம்',          20),
        ('PAYMENT_METHOD', 'CHEQUE',           N'Cheque',                 N'චෙක්පත',                 N'காசோலை',                    30),
        ('PAYMENT_METHOD', 'CARD',             N'Card',                   N'කාඩ්පත',                 N'அட்டை',                     40),
        ('PAYMENT_METHOD', 'PAYHERE',          N'Online (PayHere)',       N'මාර්ගගත (PayHere)',       N'இணையவழி (PayHere)',          50),
        ('NEED_CATEGORY',  'DRY_RATIONS',      N'Dry rations',            N'වියළි ආහාර',              N'உலர் உணவுப் பொருட்கள்',      10),
        ('NEED_CATEGORY',  'STATIONERY',       N'Stationery',             N'ලිපි ද්‍රව්‍ය',              N'எழுதுபொருட்கள்',             20),
        ('NEED_CATEGORY',  'TOILETRIES',       N'Toiletries',             N'සනීපාරක්ෂක ද්‍රව්‍ය',         N'கழிப்பறைப் பொருட்கள்',        30),
        ('NEED_CATEGORY',  'MEDICINE',         N'Medicine',               N'ඖෂධ',                   N'மருந்துகள்',                 40),
        ('NEED_CATEGORY',  'CLOTHING',         N'Clothing',               N'ඇඳුම්',                  N'ஆடைகள்',                    50),
        ('NEED_CATEGORY',  'KITCHEN_CLEANING', N'Kitchen and cleaning',   N'මුළුතැන්ගෙයි සහ පිරිසිදු කිරීමේ ද්‍රව්‍ය', N'சமையலறை மற்றும் சுத்தம் செய்யும் பொருட்கள்', 60),
        ('NEED_CATEGORY',  'OTHER',            N'Other',                  N'වෙනත්',                  N'மற்றவை',                    70),
        ('UNIT',           'PIECES',           N'Pieces',                 N'කෑලි',                   N'துண்டுகள்',                 10),
        ('UNIT',           'PACKETS',          N'Packets',                N'පැකට්',                  N'பொதிகள்',                   20),
        ('UNIT',           'KG',               N'Kilograms',              N'කිලෝග්‍රෑම්',               N'கிலோகிராம்',                30),
        ('UNIT',           'LITRES',           N'Litres',                 N'ලීටර්',                  N'லிட்டர்',                   40),
        ('UNIT',           'BOXES',            N'Boxes',                  N'පෙට්ටි',                 N'பெட்டிகள்',                 50),
        ('UNIT',           'SETS',             N'Sets',                   N'කට්ටල',                  N'தொகுப்புகள்',               60)
    ) AS v(TypeCode, Code, Name, NameSi, NameTa, SortOrder)
    JOIN core.LookupTypes lt ON lt.Code = v.TypeCode
) AS s
ON t.LookupTypeId = s.LookupTypeId AND t.Code = s.Code
-- Compared as binary, as the templates are: a zero-width joiner, which Sinhala conjuncts such as
-- ද්‍රව්‍ය depend on, can compare as equal to its absence under the database's collation.
WHEN MATCHED AND (t.Name COLLATE Latin1_General_BIN2 <> s.Name COLLATE Latin1_General_BIN2
                  OR ISNULL(t.NameSi, N'') COLLATE Latin1_General_BIN2 <> s.NameSi COLLATE Latin1_General_BIN2
                  OR ISNULL(t.NameTa, N'') COLLATE Latin1_General_BIN2 <> s.NameTa COLLATE Latin1_General_BIN2
                  OR t.SortOrder <> s.SortOrder OR t.IsActive = 0 OR t.ParentId IS NOT NULL)
    THEN UPDATE SET Name = s.Name, NameSi = s.NameSi, NameTa = s.NameTa, SortOrder = s.SortOrder, IsActive = 1, ParentId = NULL
WHEN NOT MATCHED THEN
    INSERT (LookupTypeId, Code, Name, NameSi, NameTa, SortOrder, IsActive)
    VALUES (s.LookupTypeId, s.Code, s.Name, s.NameSi, s.NameTa, s.SortOrder, 1);
