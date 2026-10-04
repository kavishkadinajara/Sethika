/*  SMS templates for one-time codes (plan 5.6): one per OtpPurpose that sends an SMS today - signup
    (/api/auth/otp/request) and stepup (/api/auth/stepup/request) - in en, si and ta. login and reset
    have no endpoint that sends a code yet; their templates arrive with those flows.

    OtpService reads a body by (Code, Channel, Language), falls back to 'en' with a warning when the
    language has none, and refuses to send at all when 'en' is missing too. {code} is the only
    placeholder. The wording started from the hard-coded texts these replace, so nothing a user
    receives changed with the move.

    Every body fits one SMS segment, because each extra segment is paid for: English is GSM-7
    (160 characters a segment), Sinhala and Tamil are UCS-2 (70). Sent with a six-digit code:
    en 40, si 34, ta 45. Keep any new body under those limits.

    Matched on (Code, Channel, Language), never on Id: notify.Notifications.TemplateId points at
    these rows, so they are updated in place, and rows another seed owns are never deleted.

    The si and ta bodies are drafts until a native speaker signs off docs/translations-to-review.md.

    Saved as UTF-8 with a BOM so the Sinhala and Tamil survive the build on any machine's code page. */
MERGE notify.Templates AS t
USING (VALUES
 ('OTP_SIGNUP', 'SMS', 'en', N'Your Sethika verification code is {code}'),
 ('OTP_SIGNUP', 'SMS', 'si', N'ඔබගේ Sethika සත්‍යාපන කේතය: {code}'),
 ('OTP_SIGNUP', 'SMS', 'ta', N'உங்கள் Sethika சரிபார்ப்புக் குறியீடு: {code}'),
 ('OTP_STEPUP', 'SMS', 'en', N'Your Sethika verification code is {code}'),
 ('OTP_STEPUP', 'SMS', 'si', N'ඔබගේ Sethika සත්‍යාපන කේතය: {code}'),
 ('OTP_STEPUP', 'SMS', 'ta', N'உங்கள் Sethika சரிபார்ப்புக் குறியீடு: {code}')
) AS s(Code, Channel, Language, Body)
ON t.Code = s.Code AND t.Channel = s.Channel AND t.Language = s.Language
-- Compared as binary: under the database's case-insensitive collation a case-only edit would never
-- deploy, and a zero-width joiner - which Sinhala conjuncts such as සත්‍යාපන depend on - can compare
-- as equal to its absence. IsActive is left alone: switching a template off is an operator's call.
WHEN MATCHED AND t.Body COLLATE Latin1_General_BIN2 <> s.Body COLLATE Latin1_General_BIN2
    THEN UPDATE SET Body = s.Body
WHEN NOT MATCHED THEN
    INSERT (Code, Channel, Language, Subject, Body, IsActive)
    VALUES (s.Code, s.Channel, s.Language, NULL, s.Body, 1);
