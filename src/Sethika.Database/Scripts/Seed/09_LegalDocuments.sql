/*  Legal documents a user accepts (plan Q4, decided 2026-09-27).

    0.1-draft is a clearly-marked PLACEHOLDER: no real Terms of Use or Privacy Policy exists yet,
    and signup cannot record consent without a document row, because auth.UserConsents
    .LegalDocumentId is NOT NULL. Every title and body says DRAFT, so no one can mistake it for
    agreed terms. The real text arrives as a new Version with IsCurrent = 1; consents already given
    keep pointing at the draft they accepted.

    A version is never updated once seeded - only inserted. Its ContentSha256 is the proof of what
    a user accepted, so editing the text of an existing version would make every consent against it
    point at words the user never saw. Change the text, change the Version.

    ContentSha256 is SHA-256 over the NVARCHAR content as stored - UTF-16LE bytes - in lowercase
    hex. Anything that re-verifies it must hash the same encoding.

    Saved as UTF-8 with a BOM so the Sinhala and Tamil survive the build on any machine's code page. */
MERGE core.LegalDocuments AS t
USING (
    SELECT v.Code, v.Version, v.Language, v.Title, v.Content, v.EffectiveFrom,
           LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256', v.Content), 2)) AS ContentSha256
    FROM (VALUES
 ('TERMS', '0.1-draft', 'en',
  N'DRAFT — Sethika Terms of Use (0.1-draft)',
  N'DRAFT — NOT FINAL LEGAL TEXT.' + NCHAR(10) + N'' + NCHAR(10) + N'This is a placeholder for the Sethika Terms of Use while the real terms are written and reviewed. By continuing, you confirm that you have seen this draft. When the final terms are published, you will be asked to accept them before you continue to use Sethika.',
  '2026-09-27'),
 ('PRIVACY', '0.1-draft', 'en',
  N'DRAFT — Sethika Privacy Policy (0.1-draft)',
  N'DRAFT — NOT FINAL LEGAL TEXT.' + NCHAR(10) + N'' + NCHAR(10) + N'This is a placeholder for the Sethika Privacy Policy while the real policy is written and reviewed. By continuing, you confirm that you have seen this draft. When the final policy is published, you will be asked to accept it before you continue to use Sethika.',
  '2026-09-27'),
 ('TERMS', '0.1-draft', 'si',
  N'කෙටුම්පත — Sethika භාවිත නියමයන් (0.1-draft)',
  N'කෙටුම්පත — අවසාන නීතිමය පාඨය නොවේ.' + NCHAR(10) + N'' + NCHAR(10) + N'සැබෑ නියමයන් ලියා සමාලෝචනය කරන තුරු, මෙය Sethika භාවිත නියමයන් වෙනුවට තබා ඇති තාවකාලික පාඨයකි. ඉදිරියට යාමෙන්, ඔබ මෙම කෙටුම්පත දුටු බව තහවුරු කරයි. අවසාන නියමයන් ප්‍රකාශයට පත් කළ විට, Sethika දිගටම භාවිත කිරීමට පෙර ඒවා පිළිගන්නා ලෙස ඔබෙන් ඉල්ලනු ලැබේ.',
  '2026-09-27'),
 ('PRIVACY', '0.1-draft', 'si',
  N'කෙටුම්පත — Sethika පෞද්ගලිකත්ව ප්‍රතිපත්තිය (0.1-draft)',
  N'කෙටුම්පත — අවසාන නීතිමය පාඨය නොවේ.' + NCHAR(10) + N'' + NCHAR(10) + N'සැබෑ ප්‍රතිපත්තිය ලියා සමාලෝචනය කරන තුරු, මෙය Sethika පෞද්ගලිකත්ව ප්‍රතිපත්තිය වෙනුවට තබා ඇති තාවකාලික පාඨයකි. ඉදිරියට යාමෙන්, ඔබ මෙම කෙටුම්පත දුටු බව තහවුරු කරයි. අවසාන ප්‍රතිපත්තිය ප්‍රකාශයට පත් කළ විට, Sethika දිගටම භාවිත කිරීමට පෙර එය පිළිගන්නා ලෙස ඔබෙන් ඉල්ලනු ලැබේ.',
  '2026-09-27'),
 ('TERMS', '0.1-draft', 'ta',
  N'வரைவு — Sethika பயன்பாட்டு விதிமுறைகள் (0.1-draft)',
  N'வரைவு — இறுதி சட்ட உரை அல்ல.' + NCHAR(10) + N'' + NCHAR(10) + N'உண்மையான விதிமுறைகள் எழுதப்பட்டு மறுஆய்வு செய்யப்படும் வரை, இது Sethika பயன்பாட்டு விதிமுறைகளுக்குப் பதிலாக வைக்கப்பட்டுள்ள தற்காலிக உரை. தொடர்வதன் மூலம், இந்த வரைவை நீங்கள் பார்த்துள்ளீர்கள் என்பதை உறுதிப்படுத்துகிறீர்கள். இறுதி விதிமுறைகள் வெளியிடப்படும்போது, Sethika-வைத் தொடர்ந்து பயன்படுத்துவதற்கு முன் அவற்றை ஏற்றுக்கொள்ளுமாறு கேட்கப்படுவீர்கள்.',
  '2026-09-27'),
 ('PRIVACY', '0.1-draft', 'ta',
  N'வரைவு — Sethika தனியுரிமைக் கொள்கை (0.1-draft)',
  N'வரைவு — இறுதி சட்ட உரை அல்ல.' + NCHAR(10) + N'' + NCHAR(10) + N'உண்மையான கொள்கை எழுதப்பட்டு மறுஆய்வு செய்யப்படும் வரை, இது Sethika தனியுரிமைக் கொள்கைக்குப் பதிலாக வைக்கப்பட்டுள்ள தற்காலிக உரை. தொடர்வதன் மூலம், இந்த வரைவை நீங்கள் பார்த்துள்ளீர்கள் என்பதை உறுதிப்படுத்துகிறீர்கள். இறுதிக் கொள்கை வெளியிடப்படும்போது, Sethika-வைத் தொடர்ந்து பயன்படுத்துவதற்கு முன் அதை ஏற்றுக்கொள்ளுமாறு கேட்கப்படுவீர்கள்.',
  '2026-09-27'),
 /*  What an applicant declares on submitting an institution application (plan 02, B3/B9). */
 ('INSTITUTION_DECLARATION', '0.1-draft', 'en',
  N'DRAFT — Sethika Institution Declaration (0.1-draft)',
  N'DRAFT — NOT FINAL LEGAL TEXT.' + NCHAR(10) + N'' + NCHAR(10) + N'This is a placeholder for the declaration an applicant makes when registering an institution on Sethika, while the real text is written and reviewed. By submitting, you confirm that the details and documents in this application are true, that the institution has authorised you to apply on its behalf, and that you have seen this draft.',
  '2026-10-18'),
 ('INSTITUTION_DECLARATION', '0.1-draft', 'si',
  N'කෙටුම්පත — Sethika ආයතන ප්‍රකාශය (0.1-draft)',
  N'කෙටුම්පත — අවසාන නීතිමය පාඨය නොවේ.' + NCHAR(10) + N'' + NCHAR(10) + N'සැබෑ පාඨය ලියා සමාලෝචනය කරන තුරු, මෙය Sethika හි ආයතනයක් ලියාපදිංචි කිරීමේදී අයදුම්කරු කරන ප්‍රකාශය වෙනුවට තබා ඇති තාවකාලික පාඨයකි. ඉදිරිපත් කිරීමෙන්, මෙම අයදුම්පතේ ඇති විස්තර සහ ලේඛන සත්‍ය බවත්, ආයතනය වෙනුවෙන් අයදුම් කිරීමට ආයතනය ඔබට බලය දී ඇති බවත්, ඔබ මෙම කෙටුම්පත දුටු බවත් ඔබ තහවුරු කරයි.',
  '2026-10-18'),
 ('INSTITUTION_DECLARATION', '0.1-draft', 'ta',
  N'வரைவு — Sethika நிறுவன உறுதிமொழி (0.1-draft)',
  N'வரைவு — இறுதி சட்ட உரை அல்ல.' + NCHAR(10) + N'' + NCHAR(10) + N'உண்மையான உரை எழுதப்பட்டு மறுஆய்வு செய்யப்படும் வரை, இது Sethika-வில் ஒரு நிறுவனத்தைப் பதிவு செய்யும்போது விண்ணப்பதாரர் அளிக்கும் உறுதிமொழிக்குப் பதிலாக வைக்கப்பட்டுள்ள தற்காலிக உரை. சமர்ப்பிப்பதன் மூலம், இந்த விண்ணப்பத்திலுள்ள விவரங்களும் ஆவணங்களும் உண்மையானவை என்பதையும், நிறுவனத்தின் சார்பில் விண்ணப்பிக்க நிறுவனம் உங்களுக்கு அதிகாரம் அளித்துள்ளது என்பதையும், இந்த வரைவை நீங்கள் பார்த்துள்ளீர்கள் என்பதையும் உறுதிப்படுத்துகிறீர்கள்.',
  '2026-10-18')
    ) AS v(Code, Version, Language, Title, Content, EffectiveFrom)
) AS s
ON t.Code = s.Code AND t.Version = s.Version AND t.Language = s.Language
WHEN NOT MATCHED THEN
    INSERT (Code, Version, Language, Title, Content, ContentSha256, EffectiveFrom, IsCurrent)
    VALUES (s.Code, s.Version, s.Language, s.Title, s.Content, s.ContentSha256, s.EffectiveFrom, 1);
