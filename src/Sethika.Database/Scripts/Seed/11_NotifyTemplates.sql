/*  SMS templates for one-time codes (plan 5.6): one per OtpPurpose that sends an SMS today - signup
    (/api/auth/otp/request), stepup (/api/auth/stepup/request) and reset (/api/auth/otp/request with
    purpose reset) - in en, si and ta. A reset code says what it is for, so someone who did not ask
    to reset their password can tell from the message alone that somebody else did. login
    (/api/auth/otp/request with purpose login) says not to share the code: read out to a caller
    claiming to be Sethika, it hands over the account by itself.

    OtpService reads a body by (Code, Channel, Language), falls back to 'en' with a warning when the
    language has none, and refuses to send at all when 'en' is missing too. {code} is the only
    placeholder. The wording started from the hard-coded texts these replace, so nothing a user
    receives changed with the move.

    Every body fits one SMS segment, because each extra segment is paid for: English is GSM-7
    (160 characters a segment), Sinhala and Tamil are UCS-2 (70). Sent with a six-digit code:
    verification en 40, si 34, ta 45; reset en 42, si 44, ta 56; login en 65, si 64, ta 73.
    The Tamil sign-in text is three over and goes as two segments until a reviewer shortens it
    (docs/translations-to-review.md). Keep any new body under those limits.

    Onboarding texts (plan 02, B10), sent by the outbox processor through UserTextMessages: received,
    a reviewer's question, approved, not approved. {requestNo} is the only placeholder, and the text
    says nothing else about the application - signing in shows the rest. With a 15-character request
    number: submitted en 93, si 69, ta 78; info requested en 93, si 77, ta 84; approved en 83, si 72,
    ta 90; rejected en 74, si 70, ta 91. English is one segment; Sinhala and Tamil are one or two. At
    most four of these go to an application, so the second segment is accepted rather than cutting
    the wording to a code.

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
 ('OTP_STEPUP', 'SMS', 'ta', N'உங்கள் Sethika சரிபார்ப்புக் குறியீடு: {code}'),
 ('OTP_RESET',  'SMS', 'en', N'Your Sethika password reset code is {code}'),
 ('OTP_RESET',  'SMS', 'si', N'ඔබගේ Sethika මුරපදය යළි සැකසීමේ කේතය: {code}'),
 ('OTP_RESET',  'SMS', 'ta', N'உங்கள் Sethika கடவுச்சொல் மீட்டமைப்புக் குறியீடு: {code}'),
 ('OTP_LOGIN',  'SMS', 'en', N'Your Sethika sign-in code is {code}. Do not share it with anyone.'),
 ('OTP_LOGIN',  'SMS', 'si', N'ඔබගේ Sethika පිවිසුම් කේතය: {code}. එය කිසිවෙකු සමඟ බෙදා නොගන්න.'),
 ('OTP_LOGIN',  'SMS', 'ta', N'உங்கள் Sethika உள்நுழைவுக் குறியீடு: {code}. இதை யாருடனும் பகிர வேண்டாம்.'),
 ('ONBOARD_SUBMITTED',      'SMS', 'en', N'Sethika: we received application {requestNo}. We will text you when it has been reviewed.'),
 ('ONBOARD_SUBMITTED',      'SMS', 'si', N'Sethika: අයදුම්පත {requestNo} ලැබුණා. සමාලෝචනයෙන් පසු දන්වන්නෙමු.'),
 ('ONBOARD_SUBMITTED',      'SMS', 'ta', N'Sethika: விண்ணப்பம் {requestNo} கிடைத்தது. மதிப்பாய்வின் பின் அறிவிப்போம்.'),
 ('ONBOARD_INFO_REQUESTED', 'SMS', 'en', N'Sethika: the reviewer has a question about application {requestNo}. Sign in to answer it.'),
 ('ONBOARD_INFO_REQUESTED', 'SMS', 'si', N'Sethika: අයදුම්පත {requestNo} ගැන ප්‍රශ්නයක් ඇත. පිළිතුරු දීමට පිවිසෙන්න.'),
 ('ONBOARD_INFO_REQUESTED', 'SMS', 'ta', N'Sethika: விண்ணப்பம் {requestNo} பற்றி ஒரு கேள்வி உள்ளது. பதிலளிக்க உள்நுழையவும்.'),
 ('ONBOARD_APPROVED',       'SMS', 'en', N'Sethika: application {requestNo} is approved. Sign in to open your institution.'),
 ('ONBOARD_APPROVED',       'SMS', 'si', N'Sethika: අයදුම්පත {requestNo} අනුමතයි. ආයතනය විවෘත කිරීමට පිවිසෙන්න.'),
 ('ONBOARD_APPROVED',       'SMS', 'ta', N'Sethika: விண்ணப்பம் {requestNo} அங்கீகரிக்கப்பட்டது. நிறுவனத்தைத் திறக்க உள்நுழையவும்.'),
 ('ONBOARD_REJECTED',       'SMS', 'en', N'Sethika: application {requestNo} was not approved. Sign in to see why.'),
 ('ONBOARD_REJECTED',       'SMS', 'si', N'Sethika: අයදුම්පත {requestNo} අනුමත නොවීය. හේතුව බැලීමට පිවිසෙන්න.'),
 ('ONBOARD_REJECTED',       'SMS', 'ta', N'Sethika: விண்ணப்பம் {requestNo} அங்கீகரிக்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும்.')
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
