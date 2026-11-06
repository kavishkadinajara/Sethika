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

    Booking texts (plan 03, C11), sent to the donor through the outbox: requested, confirmed (no
    approval needed), approved, rejected, cancelled by the institution, and the day-before reminder.
    Every one starts with the same line - {bookingNo}: {institution}, {date} {time} - so a donor with
    several bookings can tell them apart, and so no placeholder sits inside a sentence where Sinhala
    or Tamil would have to inflect it. {time} is when the meal starts. The slot's name is left out on
    purpose: the institution types it freely, and free text sent from the Sethika sender id could be
    made to read as Sethika's own (a link, a payment request). The institution's name was reviewed at
    onboarding, and is given in the donor's language where the institution has one. With a
    14-character booking number, a 20-character institution, 2026-11-14 and 11:00: en 101-123 (one
    segment), si 96-119 and ta 97-128 (two). A long institution name can push Sinhala and Tamil to
    three.

    Receipt texts (plan 09, M5), sent through the outbox when a gift is recorded: to the donor's account,
    or to a donor book row's phone only if staff ticked its consent. A walk-in's typed phone is never
    texted, and a void texts nothing. Money and items are two templates rather than one {gift}
    placeholder, so no placeholder sits where Sinhala or Tamil would inflect it; the items themselves
    are typed by staff and so are never sent. {amount} is the figure as on the receipt (5,000.00).
    With RCP-2026-000001 (15), a 20-character institution and 5,000.00: money en 97, si 95, ta 95;
    items en 92, si 90, ta 88. English is one segment; Sinhala and Tamil are two.

    Matched on (Code, Channel, Language), never on Id: notify.Notifications.TemplateId points at
    these rows, so they are updated in place, and rows another seed owns are never deleted.

    Service texts (Phase E, 8.8), sent through the outbox and the 18:00 reminder job. The donor's open
    like the booking texts - {appointmentNo}: {institution}, {date} {time} - with {time} the service's
    start in Colombo; the service's own name is left out for the same reason as a slot's. Leaders
    (V14) get the same opening line at the roster's phone, in their account's language or the
    roster's. A leader may be a monk, a priest, a kurukkal or a moulavi, and each is addressed
    differently, so the Sinhala leader texts use no pronoun at all and the Tamil ones the respectful
    தாங்கள்: both are for the reviewers (docs/translations-to-review.md).

    Yearly booking texts (plan 07, H4), sent through the outbox when the yearly job cannot make a
    year's booking: every place taken, or the meal not open that day (blocked, off that weekday, out
    of season, the slot or the institution closed). There is no booking, so the opening line is
    Sethika: {institution}, {date} {time}, with the date the booking was for. A year that is booked
    is texted like any other booking. With a 20-character institution: en 140 and 149 (one segment),
    si 128-130 and ta 126-131 (two).

    The memorial reminder (plan 07, H8), sent by the reminder job to a donor whose plan has an
    observance within a week and no booking for it. It opens Sethika: {date}, {observance}, with the
    observance's name in the donor's language from core.MemorialRules. The name of the person who
    died is never sent: it is typed by the donor, and a placeholder never carries typed text. With
    the longest draft rule name: en 138 (one segment), si 110 and ta 119 (two).

    Standing sponsor texts (plan 08, K10), sent through the outbox to a regular donor: set up (naming
    the first day), a day skipped, a day given to a donor who asked, days blocked, and a swap - this
    time, or from now on. There may be no booking, so they open Sethika: {institution}, {date} {time},
    {date} being the day the text is about; a block names its first and last day let go instead of a
    time, so one text covers it however many days it takes. A donor without an account is texted at
    the phone the institution gave only if it ticked the consent box (Q2). The answer to a day request
    goes to the donor who asked and opens like a booking text: the booking number when the day was
    given, the request number when it was not. With a 20-character institution: en 133-156 (one
    segment), si 118-161 and ta 140-181 (two or three).

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
 ('ONBOARD_REJECTED',       'SMS', 'ta', N'Sethika: விண்ணப்பம் {requestNo} அங்கீகரிக்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும்.'),
 ('BOOKING_REQUESTED', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. Your request was sent. We will text you when they reply.'),
 ('BOOKING_REQUESTED', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. ඔබගේ ඉල්ලීම යැවුණා. පිළිතුර ලැබුණු විට දන්වන්නෙමු.'),
 ('BOOKING_REQUESTED', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. உங்கள் கோரிக்கை அனுப்பப்பட்டது. பதில் வந்ததும் அறிவிப்போம்.'),
 ('BOOKING_CONFIRMED', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. Your booking is confirmed. Thank you.'),
 ('BOOKING_CONFIRMED', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. ඔබගේ වෙන්කිරීම තහවුරුයි. ස්තූතියි.'),
 ('BOOKING_CONFIRMED', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. உங்கள் முன்பதிவு உறுதியானது. நன்றி.'),
 ('BOOKING_APPROVED', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. Your request was accepted. Thank you.'),
 ('BOOKING_APPROVED', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. ඔබගේ ඉල්ලීම පිළිගත්තා. ස්තූතියි.'),
 ('BOOKING_APPROVED', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. உங்கள் கோரிக்கை ஏற்கப்பட்டது. நன்றி.'),
 ('BOOKING_REJECTED', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. Your request was not accepted. Sign in to see why.'),
 ('BOOKING_REJECTED', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. ඔබගේ ඉල්ලීම පිළිගත්තේ නැත. හේතුව බැලීමට පිවිසෙන්න.'),
 ('BOOKING_REJECTED', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. உங்கள் கோரிக்கை ஏற்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும்.'),
 ('BOOKING_CANCELLED', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. The institution cancelled this booking. Sign in to see why.'),
 ('BOOKING_CANCELLED', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. ආයතනය මෙම වෙන්කිරීම අවලංගු කළා. හේතුව බැලීමට පිවිසෙන්න.'),
 ('BOOKING_CANCELLED', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. நிறுவனம் இதை ரத்து செய்தது. காரணத்தைப் பார்க்க உள்நுழையவும்.'),
 ('BOOKING_REMINDER', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. A reminder: this booking is tomorrow.'),
 ('BOOKING_REMINDER', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. මතක් කිරීමක්: මෙම වෙන්කිරීම හෙටයි.'),
 ('BOOKING_REMINDER', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. நினைவூட்டல்: இந்த முன்பதிவு நாளை.'),
 ('APPOINTMENT_REQUESTED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. Your service request was sent. We will text you when they reply.'),
 ('APPOINTMENT_REQUESTED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. ඔබගේ සේවා ඉල්ලීම යැවුණා. පිළිතුර ලැබුණු විට දන්වන්නෙමු.'),
 ('APPOINTMENT_REQUESTED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. உங்கள் சேவைக் கோரிக்கை அனுப்பப்பட்டது. பதில் வந்ததும் அறிவிப்போம்.'),
 ('APPOINTMENT_APPROVED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. Your service request was accepted. Thank you.'),
 ('APPOINTMENT_APPROVED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. ඔබගේ සේවා ඉල්ලීම පිළිගත්තා. ස්තූතියි.'),
 ('APPOINTMENT_APPROVED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. உங்கள் சேவைக் கோரிக்கை ஏற்கப்பட்டது. நன்றி.'),
 ('APPOINTMENT_REJECTED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. Your service request was not accepted. Sign in to see why.'),
 ('APPOINTMENT_REJECTED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. ඔබගේ සේවා ඉල්ලීම පිළිගත්තේ නැත. හේතුව බැලීමට පිවිසෙන්න.'),
 ('APPOINTMENT_REJECTED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. உங்கள் சேவைக் கோரிக்கை ஏற்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும்.'),
 ('APPOINTMENT_CANCELLED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. The institution cancelled this service. Sign in to see why.'),
 ('APPOINTMENT_CANCELLED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. ආයතනය මෙම සේවාව අවලංගු කළා. හේතුව බැලීමට පිවිසෙන්න.'),
 ('APPOINTMENT_CANCELLED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. நிறுவனம் இந்தச் சேவையை ரத்து செய்தது. காரணத்தைப் பார்க்க உள்நுழையவும்.'),
 ('APPOINTMENT_REMINDER', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. A reminder: this service is tomorrow.'),
 ('APPOINTMENT_REMINDER', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. මතක් කිරීමක්: මෙම සේවාව හෙටයි.'),
 ('APPOINTMENT_REMINDER', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. நினைவூட்டல்: இந்தச் சேவை நாளை.'),
 ('LEADER_ASSIGNED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. You are assigned to this service. If you cannot come, please tell the institution.'),
 ('LEADER_ASSIGNED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. මෙම සේවාව සඳහා පවරා ඇත. පැමිණිය නොහැකි නම් කරුණාකර ආයතනයට දන්වන්න.'),
 ('LEADER_ASSIGNED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. இந்தச் சேவைக்குத் தாங்கள் நியமிக்கப்பட்டுள்ளீர்கள். வர இயலாவிட்டால் தயவுசெய்து நிறுவனத்திடம் தெரிவிக்கவும்.'),
 ('LEADER_REMOVED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. You are no longer assigned to this service.'),
 ('LEADER_REMOVED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. මෙම සේවාවේ පැවරීම ඉවත් කළා.'),
 ('LEADER_REMOVED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. இந்தச் சேவைக்கான தங்கள் நியமனம் நீக்கப்பட்டது.'),
 ('LEADER_CANCELLED', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. This service was cancelled; there is no need to come.'),
 ('LEADER_CANCELLED', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. මෙම සේවාව අවලංගු කළා. පැමිණීම අවශ්‍ය නැත.'),
 ('LEADER_CANCELLED', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. இந்தச் சேவை ரத்து செய்யப்பட்டது. தாங்கள் வர வேண்டியதில்லை.'),
 ('LEADER_REMINDER', 'SMS', 'en', N'Sethika {appointmentNo}: {institution}, {date} {time}. A reminder: you are assigned to this service tomorrow.'),
 ('LEADER_REMINDER', 'SMS', 'si', N'Sethika {appointmentNo}: {institution}, {date} {time}. මතක් කිරීමක්: පවරා ඇති මෙම සේවාව හෙටයි.'),
 ('LEADER_REMINDER', 'SMS', 'ta', N'Sethika {appointmentNo}: {institution}, {date} {time}. நினைவூட்டல்: தாங்கள் நியமிக்கப்பட்ட இந்தச் சேவை நாளை.'),
 ('RECURRING_FULL', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. Your yearly booking could not be made: every place is taken. Sign in to choose another day.'),
 ('RECURRING_FULL', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. වාර්ෂික වෙන්කිරීම කළ නොහැකි විය: ස්ථාන සියල්ල වෙන් කර ඇත. වෙනත් දිනයකට පිවිසෙන්න.'),
 ('RECURRING_FULL', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. வருடாந்த முன்பதிவு முடியவில்லை: இடங்கள் நிரம்பின. வேறு நாளுக்கு உள்நுழையவும்.'),
 ('RECURRING_CLOSED', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. Your yearly booking could not be made: the meal is not open that day. Sign in to choose another day.'),
 ('RECURRING_CLOSED', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. වාර්ෂික වෙන්කිරීම කළ නොහැකි විය: එදින මෙම ආහාර වේල නැත. වෙනත් දිනයකට පිවිසෙන්න.'),
 ('RECURRING_CLOSED', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. வருடாந்த முன்பதிவு முடியவில்லை: அன்று இந்த வேளை இல்லை. வேறு நாளுக்கு உள்நுழையவும்.'),
 ('MEMORIAL_REMINDER', 'SMS', 'en', N'Sethika: {date}, {observance}. A memorial day in your plan is near and not booked yet. Sign in to find a place to give.'),
 ('MEMORIAL_REMINDER', 'SMS', 'si', N'Sethika: {date}, {observance}. ඔබේ සමරු දිනයක් ළඟයි, තවම වෙන් කර නැත. ස්ථානයක් සොයන්න පිවිසෙන්න.'),
 ('MEMORIAL_REMINDER', 'SMS', 'ta', N'Sethika: {date}, {observance}. உங்கள் நினைவு நாள் நெருங்குகிறது, முன்பதிவு இல்லை. இடம் தேட உள்நுழையவும்.'),
 ('STANDING_CREATED', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. You are now a regular donor here; this is your first day. The institution will tell you of any change.'),
 ('STANDING_CREATED', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. ඔබ දැන් මෙම ආයතනයේ නිත්‍ය දායකයෙකි; මෙය ඔබේ පළමු දිනයයි. වෙනසක් ඇත්නම් ආයතනය දන්වනු ඇත.'),
 ('STANDING_CREATED', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. நீங்கள் இப்போது இந்நிறுவனத்தின் வழக்கமான நன்கொடையாளர்; இது உங்கள் முதல் நாள். மாற்றம் இருந்தால் நிறுவனம் தெரிவிக்கும்.'),
 ('STANDING_SKIPPED', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. As asked, you will not give on this day; it is open to others. Your other days stay.'),
 ('STANDING_SKIPPED', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. ඉල්ලූ පරිදි මෙදින ඔබ දානය නොදෙයි; එය අන් අයට විවෘතයි. ඔබේ අනෙක් දින එලෙසම පවතී.'),
 ('STANDING_SKIPPED', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. கேட்டபடி இந்நாளில் நீங்கள் வழங்கவில்லை; இது மற்றவர்களுக்குத் திறந்துள்ளது. உங்கள் மற்ற நாட்கள் அப்படியே.'),
 ('STANDING_RELEASED', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. The institution gave this day of yours to a donor who asked for it. Your other days stay.'),
 ('STANDING_RELEASED', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. ඉල්ලීමක් කළ දායකයෙකුට ආයතනය ඔබේ මෙම දිනය ලබා දුන්නා. ඔබේ අනෙක් දින එලෙසම පවතී.'),
 ('STANDING_RELEASED', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. கேட்ட ஒரு நன்கொடையாளருக்கு நிறுவனம் உங்கள் இந்நாளை வழங்கியது. உங்கள் மற்ற நாட்கள் அப்படியே.'),
 ('STANDING_BLOCKED', 'SMS', 'en', N'Sethika: {institution}, {date} to {last}. The institution needs these days for another use, so your regular days in them are cancelled.'),
 ('STANDING_BLOCKED', 'SMS', 'si', N'Sethika: {institution}, {date} සිට {last} දක්වා. ආයතනයට මෙම දින වෙනත් කටයුත්තකට අවශ්‍ය බැවින් ඒවායේ ඔබේ නිත්‍ය දින අවලංගුයි. අනෙක් දින එලෙසම පවතී.'),
 ('STANDING_BLOCKED', 'SMS', 'ta', N'Sethika: {institution}, {date} முதல் {last} வரை. இந்நாட்கள் நிறுவனத்துக்கு வேறு தேவைக்கு வேண்டும், எனவே அவற்றில் உங்கள் வழக்கமான நாட்கள் ரத்து. மற்ற நாட்கள் அப்படியே.'),
 ('STANDING_SWAPPED', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. Your day {other} was swapped with another regular donor''s: this time, please give on {date} instead.'),
 ('STANDING_SWAPPED', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. ඔබේ {other} දිනය තවත් නිත්‍ය දායකයෙකුගේ දිනයක් සමඟ මාරු කළා: මෙවර කරුණාකර {date} දින දානය දෙන්න.'),
 ('STANDING_SWAPPED', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. உங்கள் {other} நாள் மற்றொரு வழக்கமான நன்கொடையாளரின் நாளுடன் மாற்றப்பட்டது: இம்முறை தயவுசெய்து {date} அன்று வழங்கவும்.'),
 ('STANDING_SWAPPED_ON', 'SMS', 'en', N'Sethika: {institution}, {date} {time}. Your regular days were swapped with another donor''s: from {date} on, you give on their days.'),
 ('STANDING_SWAPPED_ON', 'SMS', 'si', N'Sethika: {institution}, {date} {time}. ඔබේ නිත්‍ය දින තවත් නිත්‍ය දායකයෙකුගේ දින සමඟ මාරු කළා: {date} සිට ඔබ දානය දෙන්නේ ඔවුන්ගේ දිනවලයි.'),
 ('STANDING_SWAPPED_ON', 'SMS', 'ta', N'Sethika: {institution}, {date} {time}. உங்கள் வழக்கமான நாட்கள் மற்றொரு வழக்கமான நன்கொடையாளரின் நாட்களுடன் மாற்றப்பட்டன: {date} முதல் அவர்களின் நாட்களில் வழங்குவீர்கள்.'),
 ('DAY_REQUEST_GIVEN', 'SMS', 'en', N'Sethika {bookingNo}: {institution}, {date} {time}. The institution gave you the day you asked for; it is booked for you.'),
 ('DAY_REQUEST_GIVEN', 'SMS', 'si', N'Sethika {bookingNo}: {institution}, {date} {time}. ඔබ ඉල්ලූ දිනය ආයතනය ඔබට ලබා දුන්නා; එය ඔබට වෙන් කර ඇත.'),
 ('DAY_REQUEST_GIVEN', 'SMS', 'ta', N'Sethika {bookingNo}: {institution}, {date} {time}. நீங்கள் கேட்ட நாளை நிறுவனம் உங்களுக்கு வழங்கியது; அது உங்களுக்கு முன்பதிவு செய்யப்பட்டது.'),
 ('DAY_REQUEST_DECLINED', 'SMS', 'en', N'Sethika {requestNo}: {institution}, {date} {time}. The institution could not give you the day you asked for. Sign in to see why.'),
 ('DAY_REQUEST_DECLINED', 'SMS', 'si', N'Sethika {requestNo}: {institution}, {date} {time}. ඔබ ඉල්ලූ දිනය ලබා දීමට ආයතනයට නොහැකි විය. හේතුව බැලීමට පිවිසෙන්න.'),
 ('DAY_REQUEST_DECLINED', 'SMS', 'ta', N'Sethika {requestNo}: {institution}, {date} {time}. நீங்கள் கேட்ட நாளை நிறுவனத்தால் வழங்க இயலவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும்.'),
 ('DONATION_RECEIPT', 'SMS', 'en', N'Sethika {receiptNo}: {institution}, LKR {amount}. Your gift was received. Thank you.'),
 ('DONATION_RECEIPT', 'SMS', 'si', N'Sethika {receiptNo}: {institution}, රු. {amount}. ඔබේ පරිත්‍යාගය ලැබුණා. ස්තූතියි.'),
 ('DONATION_RECEIPT', 'SMS', 'ta', N'Sethika {receiptNo}: {institution}, ரூ. {amount}. உங்கள் நன்கொடை கிடைத்தது. நன்றி.'),
 ('DONATION_RECEIPT_ITEMS', 'SMS', 'en', N'Sethika {receiptNo}: {institution}. Your gift of items was received. Thank you.'),
 ('DONATION_RECEIPT_ITEMS', 'SMS', 'si', N'Sethika {receiptNo}: {institution}. ඔබේ ද්‍රව්‍ය පරිත්‍යාගය ලැබුණා. ස්තූතියි.'),
 ('DONATION_RECEIPT_ITEMS', 'SMS', 'ta', N'Sethika {receiptNo}: {institution}. உங்கள் பொருள் நன்கொடை கிடைத்தது. நன்றி.')
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
