/*  The Memorial Dana Planner's reference data (plan 07, H6, H7; the user, 2026-10-31).

    DRAFT. CLAUDE.md lists core.MemorialRules per religion as an open item, to be confirmed with
    real institutions. Until then this seeds exactly the rules the table's own comment lists, so the
    planner can be built and shown, the way 12_OnboardingDrafts.sql seeds the documents. Every
    rule's name starts with DRAFT, so no donor or reviewer mistakes it for an agreed custom, and
    docs/DECISIONS.md lists them as unconfirmed.

    An observance falls on the date of passing plus OffsetMonths, then plus OffsetDays
    (MemorialObservances in the domain). "The 7th day" is taken to count the day of passing as the
    first, so it is OffsetDays 6; whether each faith counts that way is what a reviewer confirms. A
    rule that repeats annually falls on its first date's anniversary every year after. The planner
    reads all of this from the tables: a corrected offset, a new rule or another faith is an edit
    here, not a code change.

    Religions and relationships are the lookups core.LookupTypes already defines. The relationships
    are plain family words, not facts about any faith, so they are not marked DRAFT; their Sinhala
    and Tamil names, like the religions' and the rules', are drafts in docs/translations-to-review.md.

    Only added or updated, matched on Code: retiring a value or a rule is IsActive = 0, because
    plans and bookings point at them. Names are compared as binary, as in 11_NotifyTemplates.sql: a
    zero-width joiner can compare as equal to its absence under the database's collation.

    Saved as UTF-8 with a BOM so the Sinhala and Tamil survive the build on any machine's code page. */

/* ---------------------------------------------------------------- religions and relationships */

MERGE core.LookupValues AS t
USING (
    SELECT lt.Id AS LookupTypeId, v.Code, v.Name, v.NameSi, v.NameTa, v.SortOrder
    FROM (VALUES
        ('RELIGION',     'BUDDHISM',     N'Buddhism',     N'බෞද්ධ',            N'பௌத்தம்',     10),
        ('RELIGION',     'HINDUISM',     N'Hinduism',     N'හින්දු',            N'இந்து',        20),
        ('RELIGION',     'ISLAM',        N'Islam',        N'ඉස්ලාම්',           N'இஸ்லாம்',      30),
        ('RELIGION',     'CHRISTIANITY', N'Christianity', N'ක්‍රිස්තියානි',      N'கிறிஸ்தவம்',   40),
        ('RELATIONSHIP', 'MOTHER',       N'Mother',       N'මව',               N'தாய்',         10),
        ('RELATIONSHIP', 'FATHER',       N'Father',       N'පියා',              N'தந்தை',        20),
        ('RELATIONSHIP', 'WIFE',         N'Wife',         N'බිරිඳ',             N'மனைவி',        30),
        ('RELATIONSHIP', 'HUSBAND',      N'Husband',      N'සැමියා',            N'கணவர்',        40),
        ('RELATIONSHIP', 'DAUGHTER',     N'Daughter',     N'දියණිය',            N'மகள்',         50),
        ('RELATIONSHIP', 'SON',          N'Son',          N'පුතා',              N'மகன்',         60),
        ('RELATIONSHIP', 'GRANDMOTHER',  N'Grandmother',  N'ආච්චි',             N'பாட்டி',       70),
        ('RELATIONSHIP', 'GRANDFATHER',  N'Grandfather',  N'සීයා',              N'தாத்தா',       80),
        ('RELATIONSHIP', 'SISTER',       N'Sister',       N'සහෝදරිය',           N'சகோதரி',       90),
        ('RELATIONSHIP', 'BROTHER',      N'Brother',      N'සහෝදරයා',           N'சகோதரர்',     100),
        ('RELATIONSHIP', 'RELATIVE',     N'Relative',     N'ඥාතියා',            N'உறவினர்',     110),
        ('RELATIONSHIP', 'FRIEND',       N'Friend',       N'මිතුරා',            N'நண்பர்',      120)
    ) AS v(TypeCode, Code, Name, NameSi, NameTa, SortOrder)
    JOIN core.LookupTypes lt ON lt.Code = v.TypeCode
) AS s
ON t.LookupTypeId = s.LookupTypeId AND t.Code = s.Code
WHEN MATCHED AND (t.Name COLLATE Latin1_General_BIN2 <> s.Name COLLATE Latin1_General_BIN2
                  OR ISNULL(t.NameSi, N'') COLLATE Latin1_General_BIN2 <> s.NameSi COLLATE Latin1_General_BIN2
                  OR ISNULL(t.NameTa, N'') COLLATE Latin1_General_BIN2 <> s.NameTa COLLATE Latin1_General_BIN2
                  OR t.SortOrder <> s.SortOrder OR t.IsActive = 0)
    THEN UPDATE SET Name = s.Name, NameSi = s.NameSi, NameTa = s.NameTa, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (LookupTypeId, Code, Name, NameSi, NameTa, SortOrder)
    VALUES (s.LookupTypeId, s.Code, s.Name, s.NameSi, s.NameTa, s.SortOrder);

/* ---------------------------------------------------------------- observance rules */

MERGE core.MemorialRules AS t
USING (
    SELECT r.Id AS ReligionLookupId, v.Code, v.Name, v.NameSi, v.NameTa, v.OffsetDays, v.OffsetMonths, v.RepeatsAnnually, v.SortOrder
    FROM (VALUES
        ('BUDDHISM',     'BUDDHIST_7TH_DAY',   N'DRAFT - 7th day almsgiving',  N'DRAFT - සත්වන දින දානය',  N'DRAFT - 7ஆம் நாள் தானம்',  6,  0, 0, 10),
        ('BUDDHISM',     'BUDDHIST_3_MONTHS',  N'DRAFT - 3 months almsgiving', N'DRAFT - තෙමසේ දානය',      N'DRAFT - 3 மாத தானம்',      0,  3, 0, 20),
        ('BUDDHISM',     'BUDDHIST_YEARLY',    N'DRAFT - Yearly almsgiving',   N'DRAFT - වාර්ෂික දානය',    N'DRAFT - வருடாந்த தானம்',   0, 12, 1, 30),
        ('HINDUISM',     'HINDU_31ST_DAY',     N'DRAFT - 31st day',            N'DRAFT - 31 වන දිනය',      N'DRAFT - 31ஆம் நாள்',       30, 0, 0, 10),
        ('ISLAM',        'MUSLIM_40TH_DAY',    N'DRAFT - 40th day',            N'DRAFT - 40 වන දිනය',      N'DRAFT - 40ஆம் நாள்',       39, 0, 0, 10),
        ('CHRISTIANITY', 'CHRISTIAN_40TH_DAY', N'DRAFT - 40th day',            N'DRAFT - 40 වන දිනය',      N'DRAFT - 40ஆம் நாள்',       39, 0, 0, 10)
    ) AS v(ReligionCode, Code, Name, NameSi, NameTa, OffsetDays, OffsetMonths, RepeatsAnnually, SortOrder)
    JOIN core.LookupTypes lt ON lt.Code = 'RELIGION'
    JOIN core.LookupValues r ON r.LookupTypeId = lt.Id AND r.Code = v.ReligionCode
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.ReligionLookupId <> s.ReligionLookupId
                  OR t.Name COLLATE Latin1_General_BIN2 <> s.Name COLLATE Latin1_General_BIN2
                  OR ISNULL(t.NameSi, N'') COLLATE Latin1_General_BIN2 <> s.NameSi COLLATE Latin1_General_BIN2
                  OR ISNULL(t.NameTa, N'') COLLATE Latin1_General_BIN2 <> s.NameTa COLLATE Latin1_General_BIN2
                  OR t.OffsetDays <> s.OffsetDays OR t.OffsetMonths <> s.OffsetMonths
                  OR t.RepeatsAnnually <> s.RepeatsAnnually OR t.SortOrder <> s.SortOrder OR t.IsActive = 0)
    THEN UPDATE SET ReligionLookupId = s.ReligionLookupId, Name = s.Name, NameSi = s.NameSi, NameTa = s.NameTa,
                    OffsetDays = s.OffsetDays, OffsetMonths = s.OffsetMonths, RepeatsAnnually = s.RepeatsAnnually,
                    SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (ReligionLookupId, Code, Name, NameSi, NameTa, OffsetDays, OffsetMonths, RepeatsAnnually, SortOrder)
    VALUES (s.ReligionLookupId, s.Code, s.Name, s.NameSi, s.NameTa, s.OffsetDays, s.OffsetMonths, s.RepeatsAnnually, s.SortOrder);
