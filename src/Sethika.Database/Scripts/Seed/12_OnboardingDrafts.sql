/*  Onboarding reference data (plan 02, B9, decided 2026-10-18).

    DRAFT. CLAUDE.md lists the documents each institution type must provide as an open item, to be
    confirmed with real institutions. Until then this seeds a small generic set so the flow can be
    built and demonstrated, the way 09_LegalDocuments.sql seeds a 0.1-draft of the legal texts.
    Every document type and checklist item says DRAFT in its name, so no reviewer mistakes it for
    an agreed requirement, and docs/DECISIONS.md records it as unconfirmed.

    The flow reads all of this from the tables, so the confirmed list is an edit here, not a code
    change. Document types and checklist items are only added or updated: retiring one is
    IsActive = 0, because applications already point at it. Which types require which documents is
    defined by this file alone and synced exactly, deletions included.

    The reject reasons are Sethika's own review policy, not facts about institutions, so they are
    not marked DRAFT. Their Sinhala and Tamil wording lives in the web messages, keyed by Code.  */

/* ---------------------------------------------------------------- document types */

MERGE org.DocumentTypes AS t
USING (VALUES
    ('REGISTRATION_CERTIFICATE', N'DRAFT - Registration certificate',
     N'DRAFT: the certificate issued by the registering authority, showing the registration number.', 1, 1, 0),
    ('APPLICANT_NIC',            N'DRAFT - Applicant''s NIC',
     N'DRAFT: both sides of the applicant''s National Identity Card.',                                 0, 0, 0),
    ('PROOF_OF_ADDRESS',         N'DRAFT - Proof of address',
     N'DRAFT: a recent utility bill, deed or official letter showing the institution''s address.',     0, 1, 0),
    ('AUTHORISATION_LETTER',     N'DRAFT - Letter of authorisation',
     N'DRAFT: a letter from the governing body naming the applicant as authorised to register the institution.', 0, 1, 0)
) AS s(Code, Name, Description, RequiresReferenceNo, RequiresIssueDate, RequiresExpiryDate)
ON t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR ISNULL(t.Description, N'') <> s.Description
                  OR t.RequiresReferenceNo <> s.RequiresReferenceNo OR t.RequiresIssueDate <> s.RequiresIssueDate
                  OR t.RequiresExpiryDate <> s.RequiresExpiryDate OR t.IsActive = 0)
    THEN UPDATE SET Name = s.Name, Description = s.Description, RequiresReferenceNo = s.RequiresReferenceNo,
                    RequiresIssueDate = s.RequiresIssueDate, RequiresExpiryDate = s.RequiresExpiryDate, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (Code, Name, Description, RequiresReferenceNo, RequiresIssueDate, RequiresExpiryDate)
    VALUES (s.Code, s.Name, s.Description, s.RequiresReferenceNo, s.RequiresIssueDate, s.RequiresExpiryDate);

/* ---------------------------------------------------------------- required documents per type */

/*  The same four, all mandatory, for every type, until each type's real list is confirmed.  */
DECLARE @RequiredDocuments TABLE (TypeCode VARCHAR(30) NOT NULL, DocumentCode VARCHAR(50) NOT NULL, SortOrder SMALLINT NOT NULL);

INSERT @RequiredDocuments (TypeCode, DocumentCode, SortOrder)
SELECT it.Code, d.Code, d.SortOrder
FROM org.InstitutionTypes it
CROSS JOIN (VALUES
    ('REGISTRATION_CERTIFICATE', 1),
    ('APPLICANT_NIC',            2),
    ('PROOF_OF_ADDRESS',         3),
    ('AUTHORISATION_LETTER',     4)
) AS d(Code, SortOrder)
WHERE it.Code IN ('TEMPLE', 'KOVIL', 'MOSQUE', 'CHURCH', 'ELDERS_HOME', 'CHILDRENS_HOME');

IF (SELECT COUNT(DISTINCT TypeCode) FROM @RequiredDocuments) <> 6
    THROW 51101, '12_OnboardingDrafts: an institution type code matched nothing.', 1;

IF EXISTS (SELECT 1 FROM @RequiredDocuments r
           WHERE NOT EXISTS (SELECT 1 FROM org.DocumentTypes d WHERE d.Code = r.DocumentCode))
    THROW 51102, '12_OnboardingDrafts: a required document names an unknown document type.', 1;

MERGE org.InstitutionTypeRequiredDocuments AS t
USING (
    SELECT it.Id AS InstitutionTypeId, d.Id AS DocumentTypeId, r.SortOrder,
           N'DRAFT: to be confirmed with real institutions.' AS Instructions
    FROM @RequiredDocuments r
    JOIN org.InstitutionTypes it ON it.Code = r.TypeCode
    JOIN org.DocumentTypes d     ON d.Code = r.DocumentCode
) AS s
ON t.InstitutionTypeId = s.InstitutionTypeId AND t.DocumentTypeId = s.DocumentTypeId
WHEN MATCHED AND (t.IsMandatory = 0 OR t.SortOrder <> s.SortOrder OR ISNULL(t.Instructions, N'') <> s.Instructions)
    THEN UPDATE SET IsMandatory = 1, SortOrder = s.SortOrder, Instructions = s.Instructions
WHEN NOT MATCHED BY TARGET THEN
    INSERT (InstitutionTypeId, DocumentTypeId, IsMandatory, SortOrder, Instructions)
    VALUES (s.InstitutionTypeId, s.DocumentTypeId, 1, s.SortOrder, s.Instructions)
WHEN NOT MATCHED BY SOURCE THEN DELETE;

/* ---------------------------------------------------------------- reviewer checklist */

/*  InstitutionTypeId NULL: asked of every type.  */
MERGE org.VerificationChecklistItems AS t
USING (VALUES
    ('REGISTRATION_NO_MATCHES',  N'DRAFT - The registration number matches the certificate',          10),
    ('APPLICANT_AUTHORISED',     N'DRAFT - The applicant is named on the letter of authorisation',     20),
    ('LOCATION_MATCHES_ADDRESS', N'DRAFT - The location pin matches the address',                      30),
    ('OFFICE_PHONE_ANSWERED',    N'DRAFT - The office phone was called and answered by the institution', 40)
) AS s(Code, Question, SortOrder)
ON t.Code = s.Code
WHEN MATCHED AND (t.Question <> s.Question OR t.SortOrder <> s.SortOrder OR t.InstitutionTypeId IS NOT NULL
                  OR t.IsMandatory = 0 OR t.IsActive = 0)
    THEN UPDATE SET Question = s.Question, SortOrder = s.SortOrder, InstitutionTypeId = NULL, IsMandatory = 1, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (InstitutionTypeId, Code, Question, IsMandatory, SortOrder)
    VALUES (NULL, s.Code, s.Question, 1, s.SortOrder);

/* ---------------------------------------------------------------- reject reasons */

MERGE core.LookupValues AS t
USING (
    SELECT lt.Id AS LookupTypeId, v.Code, v.Name, v.SortOrder
    FROM (VALUES
        ('DOCUMENTS_INCOMPLETE', N'Documents missing or unreadable',        10),
        ('NOT_VERIFIABLE',       N'Registration could not be verified',     20),
        ('DUPLICATE',            N'Duplicate of another application',       30),
        ('NOT_ELIGIBLE',         N'Not an eligible institution',            40),
        ('OTHER',                N'Other (explained in the note)',          90)
    ) AS v(Code, Name, SortOrder)
    JOIN core.LookupTypes lt ON lt.Code = 'REJECT_REASON'
) AS s
ON t.LookupTypeId = s.LookupTypeId AND t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR t.SortOrder <> s.SortOrder OR t.IsActive = 0)
    THEN UPDATE SET Name = s.Name, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (LookupTypeId, Code, Name, SortOrder)
    VALUES (s.LookupTypeId, s.Code, s.Name, s.SortOrder);
