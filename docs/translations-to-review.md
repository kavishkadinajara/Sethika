# Translations to review

Every Sinhala and Tamil string in `web/messages/si.json` and `web/messages/ta.json` was written
by Claude Code, not by a native speaker. **None of them is final.** A native Sinhala speaker and a
native Tamil speaker must check each row before the strings face real users.

How to use this file:

- Mark a row OK, or write the corrected text in the "Reviewer" column, then copy corrections into
  the JSON file. The key in the first column is the JSON path.
- When a component adds or changes a key, add or update its row here in the same commit, with the
  Sinhala and Tamil left as drafts until reviewed.
- Delete a row only after the reviewed text is in the JSON file.

Questions for the reviewers:

1. **Brand name.** `Sethika` is kept in Latin script in all three files. Should Sinhala and Tamil
   pages write it in their own script, and if so, how is it spelled?
2. **Faith-neutral wording for "meal".** The tagline avoids `දානය` (dana), which reads as a
   Buddhist offering, and uses `ආහාර වේල` / `உணவு வேளை` ("a meal"). Is that natural, and is there a
   better word that every faith in Sri Lanka would read as its own?
3. **Place names for each faith.** Sinhala uses පන්සල (temple), කෝවිල (kovil), මුස්ලිම් පල්ලිය
   (mosque), කිතුනු දේවස්ථානය (church). Tamil uses விகாரை (Buddhist temple), கோவில் (kovil),
   பள்ளிவாசல் (mosque), தேவாலயம் (church). Are these the terms each community uses for itself?
4. **"Sign in".** Sinhala `පුරනය වන්න` and Tamil `உள்நுழைக` follow common software usage. Confirm
   they read naturally to someone who is not used to computers.
5. **Developer-facing strings** (`Home.*`, `PermissionDebug.*`, `Design.*`) belong to the
   permission debug page and the `/design` review page; review them last. In them, `paste`, `Tab`,
   `Escape`, `hover`, `focus` and `Estate Green` were left in English on purpose (key names and
   colour names). Say if Sinhala or Tamil readers expect a native word instead.
6. **Language names** (`LanguageSwitcher.names.*`) are written in their own script in every file
   (English / සිංහල / தமிழ்), which is the usual convention. Confirm.
7. **Short labels on controls** (`Common.close`, `Common.required`, `Common.loading`, `Toast.*`) are
   read aloud by screen readers. Check they sound natural spoken, not only written.
8. **Theme names** (`ThemeSwitcher.*`): "Auto" means "follow the phone's light/dark setting".
   Sinhala `ස්වයංක්‍රීය` and Tamil `தானியங்கி` say "automatic"; check that a reader understands
   it as "same as my phone". `தீம்` and `තේමාව` for "Theme" are loanword choices; confirm.
9. **Auth screens** (`Login.*`, `Signup.*`, `StepUp.*`, `SelectInstitution.*`, `Session.*`,
   `Errors.*`, `Password.*`, `PasswordChange.*`): these are the first words most donors read. Check especially that
   - the password rules say "English letter" (`ඉංග්‍රීසි අකුරක්`, `ஆங்கில எழுத்து`) on purpose: the API
     counts only letters with upper and lower case, so Sinhala or Tamil letters do not satisfy
     "uppercase" or "lowercase";
   - "sign in" / "sign out" (`පුරනය වන්න` / `ඉවත් වන්න`, `உள்நுழைக` / `வெளியேறு`) match what
     donors see in other Sri Lankan apps;
   - the plural forms in `Errors.otp_incorrect` read naturally for 1 and for 2-4 attempts left;
   - `PasswordChange.description` and `PasswordChange.changedDescription` say that *other* devices
     are signed out and this one is not; check that the drafts cannot be read as "you will be
     signed out".
10. **Forgotten password** (`ForgotPassword.*`, `Login.forgotPassword`, `Errors.reset_ticket_invalid`):
   `ForgotPassword.codeSentTo` says "*if* this number has an account, a code is on its way" on
   purpose. The API does not say whether a number is registered, and the screen must not either.
   Keep it conditional in the reviewed text: not "we sent", and not "no account found".

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `Metadata.title` | Sethika | Sethika | Sethika | | |
| `Brand.name` | Sethika | Sethika | Sethika | | |
| `Brand.tagline` | Sponsor a meal at a temple, kovil, mosque, church or care home in Sri Lanka. | ශ්‍රී ලංකාවේ පන්සලක, කෝවිලක, මුස්ලිම් පල්ලියක, කිතුනු දේවස්ථානයක හෝ සත්කාර නිවාසයක ආහාර වේලකට අනුග්‍රහය දක්වන්න. | இலங்கையில் ஒரு விகாரை, கோவில், பள்ளிவாசல், தேவாலயம் அல்லது பராமரிப்பு இல்லத்தில் ஒரு வேளை உணவுக்கு அனுசரணை வழங்குங்கள். | | |
| `Home.intro` | Permission engine wiring. Controls appear only for codes the signed-in user actually holds. | අවසර එන්ජිම සම්බන්ධ කිරීම. පුරනය වූ පරිශීලකයාට සැබවින්ම ඇති කේත සඳහා පමණක් පාලන දිස්වේ. | அனுமதி இயந்திர இணைப்பு. உள்நுழைந்த பயனரிடம் உண்மையில் உள்ள குறியீடுகளுக்கு மட்டுமே கட்டுப்பாடுகள் தோன்றும். | | |
| `Home.calendarHeading` | Booking calendar | වෙන්කිරීම් දින දර්ශනය | முன்பதிவு நாட்காட்டி | | |
| `Home.calendarDenied` | You do not have access to the booking calendar. | වෙන්කිරීම් දින දර්ශනයට ඔබට ප්‍රවේශය නැත. | முன்பதிவு நாட்காட்டியை அணுக உங்களுக்கு அனுமதி இல்லை. | | |
| `Home.calendarPlaceholder` | Calendar would render here. | දින දර්ශනය මෙහි දිස්වනු ඇත. | நாட்காட்டி இங்கே காண்பிக்கப்படும். | | |
| `Home.approveBooking` | Approve booking | වෙන්කිරීම අනුමත කරන්න | முன்பதிவை அங்கீகரிக்கவும் | | |
| `PermissionDebug.heading` | Resolved context | නිර්ණය කළ සන්දර්භය | தீர்மானிக்கப்பட்ட சூழல் | | |
| `PermissionDebug.signedIn` | Signed in | පුරනය වී ඇත | உள்நுழைந்துள்ளார் | | |
| `PermissionDebug.platformStaff` | Platform staff | වේදිකා කාර්ය මණ්ඩලය | தள ஊழியர் | | |
| `PermissionDebug.institution` | Institution | ආයතනය | நிறுவனம் | | |
| `PermissionDebug.yes` | yes | ඔව් | ஆம் | | |
| `PermissionDebug.no` | no | නැත | இல்லை | | |
| `PermissionDebug.none` | none | කිසිවක් නැත | எதுவும் இல்லை | | |
| `PermissionDebug.granted` | granted | ලබා දී ඇත | வழங்கப்பட்டது | | |
| `PermissionDebug.denied` | denied | ප්‍රතික්ෂේපිතයි | மறுக்கப்பட்டது | | |
| `PermissionDebug.signedOutNote` | The web app does not issue an access token yet (the sign-in screens arrive with slice 5.7), so it renders signed out and every gate is closed. | වෙබ් යෙදුම තවම ප්‍රවේශ ටෝකනයක් නිකුත් නොකරයි (පුරනය වීමේ තිර 5.7 කොටස සමඟ පැමිණේ). එබැවින් එය පුරනය නොවූ ලෙස දිස්වන අතර සියලු දොරටු වසා ඇත. | இணையப் பயன்பாடு இன்னும் அணுகல் டோக்கனை வழங்கவில்லை (உள்நுழைவுத் திரைகள் 5.7 பகுதியுடன் வரும்). எனவே அது வெளியேறிய நிலையில் காட்டப்படுகிறது, அனைத்து வாயில்களும் மூடப்பட்டுள்ளன. | | |
| `Login.title` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `Login.comingSoon` | Signing in will be available soon. | පුරනය වීම ඉක්මනින් ලබා ගත හැකි වේ. | உள்நுழைவு விரைவில் கிடைக்கும். | | |
| `Login.subtitle` | Welcome back. | නැවත සාදරයෙන් පිළිගනිමු. | மீண்டும் வருக. | | |
| `Login.loginLabel` | Phone number or email | දුරකථන අංකය හෝ විද්‍යුත් තැපෑල | தொலைபேசி எண் அல்லது மின்னஞ்சல் | | |
| `Login.loginHelp` | The mobile number or email you signed up with. | ඔබ ලියාපදිංචි වූ ජංගම අංකය හෝ විද්‍යුත් තැපෑල. | நீங்கள் பதிவுசெய்த கைபேசி எண் அல்லது மின்னஞ்சல். | | |
| `Login.loginRequired` | Enter your phone number or email. | ඔබගේ දුරකථන අංකය හෝ විද්‍යුත් තැපෑල ඇතුළත් කරන්න. | உங்கள் தொலைபேசி எண் அல்லது மின்னஞ்சலை உள்ளிடுங்கள். | | |
| `Login.passwordLabel` | Password | මුරපදය | கடவுச்சொல் | | |
| `Login.passwordRequired` | Enter your password. | ඔබගේ මුරපදය ඇතුළත් කරන්න. | உங்கள் கடவுச்சொல்லை உள்ளிடுங்கள். | | |
| `Login.submit` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `Login.noAccount` | New to Sethika? | Sethika වෙත අලුත්ද? | Sethika-வுக்குப் புதியவரா? | | |
| `Login.createAccount` | Create an account | ගිණුමක් සාදන්න | கணக்கை உருவாக்குங்கள் | | |
| `NotFound.title` | Page not found | පිටුව හමු නොවීය | பக்கம் கிடைக்கவில்லை | | |
| `NotFound.home` | Go to the home page | මුල් පිටුවට යන්න | முகப்புப் பக்கத்திற்குச் செல்லவும் | | |
| `Common.close` | Close | වසන්න | மூடு | | |
| `Common.loading` | Loading | පූරණය වෙමින් | ஏற்றப்படுகிறது | | |
| `Common.required` | required | අනිවාර්යයි | கட்டாயம் | | |
| `Common.showPassword` | Show password | මුරපදය පෙන්වන්න | கடவுச்சொல்லைக் காட்டு | | |
| `Common.hidePassword` | Hide password | මුරපදය සඟවන්න | கடவுச்சொல்லை மறை | | |
| `Toast.region` | Notifications ({hotkey}) | දැනුම්දීම් ({hotkey}) | அறிவிப்புகள் ({hotkey}) | | |
| `Toast.item` | Notification | දැනුම්දීම | அறிவிப்பு | | |
| `LanguageSwitcher.label` | Language | භාෂාව | மொழி | | |
| `LanguageSwitcher.names.en` | English | English | English | | |
| `LanguageSwitcher.names.si` | සිංහල | සිංහල | සිංහල | | |
| `LanguageSwitcher.names.ta` | தமிழ் | தமிழ் | தமிழ் | | |
| `ThemeSwitcher.label` | Theme | තේමාව | தீம் | | |
| `ThemeSwitcher.system` | Auto | ස්වයංක්‍රීය | தானியங்கி | | |
| `ThemeSwitcher.light` | Light | ආලෝකමත් | வெளிர் | | |
| `ThemeSwitcher.dark` | Dark | අඳුරු | இருண்ட | | |
| `Design.title` | Design system | නිර්මාණ පද්ධතිය | வடிவமைப்பு அமைப்பு | | |
| `Design.intro` | Every primitive in every state, for review. This page is hidden from search engines. | සමාලෝචනය සඳහා, සෑම මූලික සංරචකයක්ම සෑම තත්ත්වයකින්ම. මෙම පිටුව සෙවුම් යන්ත්‍ර වලින් සඟවා ඇත. | மதிப்பாய்வுக்காக, ஒவ்வொரு அடிப்படைக் கூறும் ஒவ்வொரு நிலையிலும். இந்தப் பக்கம் தேடுபொறிகளிலிருந்து மறைக்கப்பட்டுள்ளது. | | |
| `Design.hint` | Hover with a mouse and move through the page with Tab to see hover and focus states. | මූසිකය ඉහළින් රඳවා, Tab යතුරෙන් පිටුව හරහා ගමන් කර hover සහ focus තත්ත්වයන් බලන්න. | hover மற்றும் focus நிலைகளைக் காண, சுட்டியை மேலே வைத்து Tab விசையால் பக்கத்தில் நகருங்கள். | | |
| `Design.sections.buttons` | Buttons | බොත්තම් | பொத்தான்கள் | | |
| `Design.sections.inputs` | Inputs | ආදාන ක්ෂේත්‍ර | உள்ளீட்டுப் புலங்கள் | | |
| `Design.sections.fields` | Fields and labels | ක්ෂේත්‍ර සහ ලේබල | புலங்களும் லேபிள்களும் | | |
| `Design.sections.cards` | Cards | කාඩ්පත් | அட்டைகள் | | |
| `Design.sections.sheets` | Sheets | පත්‍රිකා | தாள்கள் | | |
| `Design.sections.toasts` | Toasts | කෙටි දැනුම්දීම් | சிறு அறிவிப்புகள் | | |
| `Design.sections.spinners` | Spinners | පූරණ දර්ශක | ஏற்றக் குறிகாட்டிகள் | | |
| `Design.sections.otp` | One-time code | එක් වරක් භාවිත කේතය | ஒருமுறைக் குறியீடு | | |
| `Design.sections.language` | Language switcher | භාෂා මාරුව | மொழி மாற்றி | | |
| `Design.sections.theme` | Theme switcher | තේමා මාරුව | தீம் மாற்றி | | |
| `Design.states.default` | Default | සාමාන්‍ය | இயல்புநிலை | | |
| `Design.states.disabled` | Disabled | අක්‍රිය | முடக்கப்பட்டது | | |
| `Design.states.loading` | Loading | පූරණය වෙමින් | ஏற்றப்படுகிறது | | |
| `Design.states.invalid` | Invalid | වලංගු නැත | செல்லாதது | | |
| `Design.states.filled` | Filled | පුරවා ඇත | நிரப்பப்பட்டது | | |
| `Design.states.empty` | Empty | හිස් | காலி | | |
| `Design.states.partial` | Partly filled | අඩක් පුරවා ඇත | பகுதி நிரப்பப்பட்டது | | |
| `Design.states.complete` | Complete | සම්පූර්ණයි | முழுமையானது | | |
| `Design.states.long` | Long label | දිගු ලේබලය | நீண்ட லேபிள் | | |
| `Design.variants.primary` | Primary | ප්‍රධාන | முதன்மை | | |
| `Design.variants.accent` | Accent | උද්දීපන | சிறப்பு | | |
| `Design.variants.outline` | Outline | රේඛා | கோட்டுரு | | |
| `Design.variants.ghost` | Ghost | සරල | எளிய | | |
| `Design.variants.danger` | Danger | අවදානම | ஆபத்து | | |
| `Design.variants.large` | Large | විශාල | பெரியது | | |
| `Design.variants.icon` | Icon only | අයිකනය පමණි | படவுரு மட்டும் | | |
| `Design.sample.action` | Sponsor a meal | ආහාර වේලකට අනුග්‍රහය දක්වන්න | ஒரு வேளை உணவுக்கு அனுசரணை வழங்குங்கள் | | |
| `Design.sample.longAction` | Confirm the booking and send a reminder the day before | වෙන්කිරීම තහවුරු කර, පෙර දිනයේ මතක් කිරීමක් යවන්න | முன்பதிவை உறுதிசெய்து, முந்தைய நாள் நினைவூட்டலை அனுப்புங்கள் | | |
| `Design.sample.phoneLabel` | Mobile number | ජංගම දුරකථන අංකය | கைபேசி எண் | | |
| `Design.sample.phonePlaceholder` | 07X XXX XXXX | 07X XXX XXXX | 07X XXX XXXX | | |
| `Design.sample.phoneDescription` | We send a 6-digit code to this number. | අපි මෙම අංකයට ඉලක්කම් 6ක කේතයක් යවමු. | இந்த எண்ணுக்கு 6 இலக்கக் குறியீட்டை அனுப்புவோம். | | |
| `Design.sample.phoneError` | Enter a Sri Lankan mobile number, for example 0771234567. | ශ්‍රී ලංකා ජංගම දුරකථන අංකයක් ඇතුළත් කරන්න, උදාහරණයක් ලෙස 0771234567. | இலங்கைக் கைபேசி எண்ணை உள்ளிடுங்கள், எடுத்துக்காட்டாக 0771234567. | | |
| `Design.sample.nameLabel` | Full name | සම්පූර්ණ නම | முழுப் பெயர் | | |
| `Design.sample.otpLabel` | Verification code | තහවුරු කිරීමේ කේතය | சரிபார்ப்புக் குறியீடு | | |
| `Design.sample.otpDescription` | Enter the 6-digit code we sent you. You can paste it. | අපි ඔබට එවූ ඉලක්කම් 6ක කේතය ඇතුළත් කරන්න. ඔබට එය paste කළ හැක. | நாங்கள் அனுப்பிய 6 இலக்கக் குறியீட்டை உள்ளிடுங்கள். அதை paste செய்யலாம். | | |
| `Design.sample.otpError` | That code is not correct. Check the message and try again. | එම කේතය නිවැරදි නැත. පණිවිඩය පරීක්ෂා කර නැවත උත්සාහ කරන්න. | அந்தக் குறியீடு சரியானது அல்ல. செய்தியைச் சரிபார்த்து மீண்டும் முயலுங்கள். | | |
| `Design.sample.otpValue` | Entered so far: {value} | මෙතෙක් ඇතුළත් කළේ: {value} | இதுவரை உள்ளிட்டது: {value} | | |
| `Design.sample.check` | Check | පරීක්ෂා කරන්න | சரிபார் | | |
| `Design.sample.cardTitle` | Midday meal | දිවා ආහාරය | மதிய உணவு | | |
| `Design.sample.cardDescription` | A sample card: a title, a short description, content and actions. | නියැදි කාඩ්පතක්: මාතෘකාවක්, කෙටි විස්තරයක්, අන්තර්ගතය සහ ක්‍රියා. | மாதிரி அட்டை: தலைப்பு, சிறு விளக்கம், உள்ளடக்கம் மற்றும் செயல்கள். | | |
| `Design.sample.cardBody` | Cards group one thing a donor can act on. The footer holds its actions. | කාඩ්පතක් පරිත්‍යාගශීලියෙකුට කළ හැකි එක් දෙයක් එක්රැස් කරයි. පාදකයේ එහි ක්‍රියා ඇත. | நன்கொடையாளர் செய்யக்கூடிய ஒரு விடயத்தை அட்டை ஒன்றாகத் தொகுக்கிறது. அடிப்பகுதியில் அதன் செயல்கள் உள்ளன. | | |
| `Design.sample.cardAccentTitle` | Card with a gold accent | රන් උද්දීපනයක් සහිත කාඩ්පත | தங்க நிறச் சிறப்புடன் அட்டை | | |
| `Design.sample.cardAccentBody` | Use the accent for the one card that matters most on a screen. | තිරයක වඩාත්ම වැදගත් කාඩ්පත සඳහා පමණක් උද්දීපනය භාවිත කරන්න. | ஒரு திரையில் மிக முக்கியமான அட்டைக்கு மட்டும் இந்தச் சிறப்பைப் பயன்படுத்துங்கள். | | |
| `Design.sample.openBottom` | Open a bottom sheet | පහළ පත්‍රිකාවක් විවෘත කරන්න | கீழ்த் தாளைத் திறக்கவும் | | |
| `Design.sample.openRight` | Open a side sheet | පැති පත්‍රිකාවක් විවෘත කරන්න | பக்கத் தாளைத் திறக்கவும் | | |
| `Design.sample.sheetTitle` | Sheet title | පත්‍රිකා මාතෘකාව | தாள் தலைப்பு | | |
| `Design.sample.sheetDescription` | Sheets hold a short task without leaving the page. Press Escape or the close button to dismiss. | පත්‍රිකා පිටුවෙන් ඉවත් නොවී කෙටි කාර්යයක් සඳහා යොදා ගනී. වසා දැමීමට Escape හෝ වසන්න බොත්තම ඔබන්න. | பக்கத்தை விட்டு வெளியேறாமல் ஒரு சிறு பணிக்குத் தாள்கள் உதவும். மூட Escape அல்லது மூடு பொத்தானை அழுத்துங்கள். | | |
| `Design.sample.done` | Done | අවසන් | முடிந்தது | | |
| `Design.sample.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Design.sample.showDefault` | Show a notice | දැනුම්දීමක් පෙන්වන්න | அறிவிப்பைக் காட்டு | | |
| `Design.sample.showSuccess` | Show a success | සාර්ථක පණිවිඩයක් පෙන්වන්න | வெற்றிச் செய்தியைக் காட்டு | | |
| `Design.sample.showError` | Show an error | දෝෂයක් පෙන්වන්න | பிழையைக் காட்டு | | |
| `Design.sample.noticeTitle` | Reminder saved | මතක් කිරීම සුරකින ලදී | நினைவூட்டல் சேமிக்கப்பட்டது | | |
| `Design.sample.noticeBody` | We will text you the day before. | අපි ඔබට පෙර දිනයේ කෙටි පණිවිඩයක් එවන්නෙමු. | முந்தைய நாள் உங்களுக்குக் குறுஞ்செய்தி அனுப்புவோம். | | |
| `Design.sample.successTitle` | Booking confirmed | වෙන්කිරීම තහවුරු විය | முன்பதிவு உறுதிசெய்யப்பட்டது | | |
| `Design.sample.successBody` | The institution has been told. | ආයතනයට දැනුම් දී ඇත. | நிறுவனத்துக்குத் தெரிவிக்கப்பட்டுள்ளது. | | |
| `Design.sample.errorTitle` | Could not send the code | කේතය යැවිය නොහැකි විය | குறியீட்டை அனுப்ப முடியவில்லை | | |
| `Design.sample.errorBody` | Check the number and try again in a minute. | අංකය පරීක්ෂා කර විනාඩියකින් නැවත උත්සාහ කරන්න. | எண்ணைச் சரிபார்த்து ஒரு நிமிடத்தில் மீண்டும் முயலுங்கள். | | |
| `Design.sample.onEstate` | On Estate Green | Estate Green මත | Estate Green மீது | | |
| `AuthShell.home` | Sethika home | Sethika මුල් පිටුව | Sethika முகப்பு | | |
| `Errors.network` | Can't reach Sethika right now. Check your connection and try again. | දැන් Sethika වෙත සම්බන්ධ විය නොහැක. ඔබගේ සම්බන්ධතාව පරීක්ෂා කර නැවත උත්සාහ කරන්න. | இப்போது Sethika-வை அணுக முடியவில்லை. உங்கள் இணைப்பைச் சரிபார்த்து மீண்டும் முயலுங்கள். | | |
| `Errors.unexpected` | Something went wrong. Please try again. | යමක් වැරදුණා. කරුණාකර නැවත උත්සාහ කරන්න. | ஏதோ தவறு நடந்தது. மீண்டும் முயலுங்கள். | | |
| `Errors.tooManyRequests` | Too many tries. Please wait {time} and try again. | උත්සාහයන් වැඩියි. කරුණාකර {time} ක් රැඳී සිට නැවත උත්සාහ කරන්න. | அதிகமான முயற்சிகள். {time} காத்திருந்து மீண்டும் முயலுங்கள். | | |
| `Errors.tooManyRequestsNoTime` | Too many tries. Please wait a minute and try again. | උත්සාහයන් වැඩියි. කරුණාකර විනාඩියක් රැඳී සිට නැවත උත්සාහ කරන්න. | அதிகமான முயற்சிகள். ஒரு நிமிடம் காத்திருந்து மீண்டும் முயலுங்கள். | | |
| `Errors.invalid_phone_number` | Enter a Sri Lankan mobile number, for example 077 123 4567. | ශ්‍රී ලංකා ජංගම දුරකථන අංකයක් ඇතුළත් කරන්න, උදාහරණයක් ලෙස 077 123 4567. | இலங்கைக் கைபேசி எண்ணை உள்ளிடுங்கள், எடுத்துக்காட்டாக 077 123 4567. | | |
| `Errors.invalid_full_name` | Enter your name (up to 150 characters). | ඔබගේ නම ඇතුළත් කරන්න (අකුරු 150 දක්වා). | உங்கள் பெயரை உள்ளிடுங்கள் (150 எழுத்துகள் வரை). | | |
| `Errors.invalid_email` | Enter a valid email address, or leave it empty. | වලංගු විද්‍යුත් තැපැල් ලිපිනයක් ඇතුළත් කරන්න, නැතහොත් හිස්ව තබන්න. | சரியான மின்னஞ்சல் முகவரியை உள்ளிடுங்கள், அல்லது காலியாக விடுங்கள். | | |
| `Errors.password_rejected` | Please choose a stronger password. | කරුණාකර වඩා ශක්තිමත් මුරපදයක් තෝරන්න. | இன்னும் வலுவான கடவுச்சொல்லைத் தேர்ந்தெடுங்கள். | | |
| `Errors.consent_missing` | Please accept both documents to continue. | ඉදිරියට යාමට ලේඛන දෙකම පිළිගන්න. | தொடர இரண்டு ஆவணங்களையும் ஏற்றுக்கொள்ளுங்கள். | | |
| `Errors.phone_not_verified` | Your phone verification has expired. Please verify your number again. | ඔබගේ දුරකථන තහවුරු කිරීම කල් ඉකුත් වී ඇත. කරුණාකර ඔබගේ අංකය නැවත තහවුරු කරන්න. | உங்கள் தொலைபேசி உறுதிப்படுத்தல் காலாவதியாகிவிட்டது. உங்கள் எண்ணை மீண்டும் உறுதிப்படுத்துங்கள். | | |
| `Errors.already_registered` | An account already uses this phone number or email. | මෙම දුරකථන අංකය හෝ විද්‍යුත් තැපෑල දැනටමත් ගිණුමක භාවිත වේ. | இந்தத் தொலைபேசி எண் அல்லது மின்னஞ்சல் ஏற்கனவே ஒரு கணக்கில் பயன்படுத்தப்படுகிறது. | | |
| `Errors.verification_unavailable` | We can't send or check codes right now. Please try again shortly. | දැනට කේත යැවීමට හෝ පරීක්ෂා කිරීමට නොහැක. කරුණාකර මඳ වේලාවකින් නැවත උත්සාහ කරන්න. | இப்போது குறியீடுகளை அனுப்பவோ சரிபார்க்கவோ முடியவில்லை. சிறிது நேரத்தில் மீண்டும் முயலுங்கள். | | |
| `Errors.invalid_credentials` | That phone number, email or password is not right. | එම දුරකථන අංකය, විද්‍යුත් තැපෑල හෝ මුරපදය නිවැරදි නැත. | அந்தத் தொலைபேசி எண், மின்னஞ்சல் அல்லது கடவுச்சொல் சரியானது அல்ல. | | |
| `Errors.account_not_active` | This account is not active. Please contact Sethika support. | මෙම ගිණුම සක්‍රිය නැත. කරුණාකර Sethika සහාය අමතන්න. | இந்தக் கணக்கு செயலில் இல்லை. Sethika உதவிக் குழுவைத் தொடர்புகொள்ளுங்கள். | | |
| `Errors.otp_incorrect` | That code is not right. {count, plural, one {# try left} other {# tries left}}. | එම කේතය නිවැරදි නැත. {count, plural, one {තවත් එක් උත්සාහයක් ඉතිරියි} other {තවත් උත්සාහයන් #ක් ඉතිරියි}}. | அந்தக் குறியீடு சரியானது அல்ல. {count, plural, one {இன்னும் ஒரு முயற்சி உள்ளது} other {இன்னும் # முயற்சிகள் உள்ளன}}. | | |
| `Errors.otp_not_found` | This code has expired. Send a new one. | මෙම කේතය කල් ඉකුත් වී ඇත. අලුත් කේතයක් යවන්න. | இந்தக் குறியீடு காலாவதியாகிவிட்டது. புதிய குறியீட்டை அனுப்புங்கள். | | |
| `Errors.otp_attempts_exhausted` | Too many wrong codes. Send a new one. | වැරදි කේත වැඩියි. අලුත් කේතයක් යවන්න. | அதிகமான தவறான குறியீடுகள். புதிய குறியீட்டை அனுப்புங்கள். | | |
| `Errors.no_verified_phone` | Your account has no confirmed phone number, so we can't send a code. | ඔබගේ ගිණුමේ තහවුරු කළ දුරකථන අංකයක් නැති නිසා අපට කේතයක් යැවිය නොහැක. | உங்கள் கணக்கில் உறுதிப்படுத்தப்பட்ட தொலைபேசி எண் இல்லாததால் குறியீட்டை அனுப்ப முடியாது. | | |
| `Errors.institution_not_available` | You can no longer act for this institution. | ඔබට තවදුරටත් මෙම ආයතනය වෙනුවෙන් ක්‍රියා කළ නොහැක. | இனி இந்த நிறுவனத்தின் சார்பில் நீங்கள் செயல்பட முடியாது. | | |
| `Errors.session_expired` | Your session has ended. Please sign in again. | ඔබගේ සැසිය අවසන් වී ඇත. කරුණාකර නැවත පුරනය වන්න. | உங்கள் அமர்வு முடிந்துவிட்டது. மீண்டும் உள்நுழையுங்கள். | | |
| `Password.title` | Your password needs: | ඔබගේ මුරපදයට අවශ්‍ය වන්නේ: | உங்கள் கடவுச்சொல்லில் இருக்க வேண்டியவை: | | |
| `Password.met` | Done: | සම්පූර්ණයි: | நிறைவேறியது: | | |
| `Password.notMet` | Not yet: | තවම නැත: | இன்னும் இல்லை: | | |
| `Password.TooShort` | At least 6 characters | අවම වශයෙන් අක්ෂර 6ක් | குறைந்தது 6 எழுத்துகள் | | |
| `Password.TooLong` | No more than 128 characters | අක්ෂර 128කට නොවැඩි | 128 எழுத்துகளுக்கு மேல் இல்லாமல் | | |
| `Password.MissingUppercase` | An uppercase letter (A–Z) | ලොකු ඉංග්‍රීසි අකුරක් (A–Z) | ஓர் ஆங்கிலப் பெரிய எழுத்து (A–Z) | | |
| `Password.MissingLowercase` | A lowercase letter (a–z) | කුඩා ඉංග්‍රීසි අකුරක් (a–z) | ஓர் ஆங்கிலச் சிறிய எழுத்து (a–z) | | |
| `Password.MissingDigit` | A number (0–9) | ඉලක්කමක් (0–9) | ஓர் எண் (0–9) | | |
| `Password.MissingNonAlphanumeric` | A symbol, such as ! or # | ! හෝ # වැනි සංකේතයක් | ! அல்லது # போன்ற ஒரு குறியீடு | | |
| `Password.ContainsPersonalData` | Not your name, phone number or email | ඔබගේ නම, දුරකථන අංකය හෝ විද්‍යුත් තැපෑල නොවිය යුතුය | உங்கள் பெயர், தொலைபேசி எண் அல்லது மின்னஞ்சல் இருக்கக்கூடாது | | |
| `Password.RecentlyUsed` | Not a password you used recently | ඔබ මෑතකදී භාවිත කළ මුරපදයක් නොවිය යුතුය | சமீபத்தில் பயன்படுத்திய கடவுச்சொல்லாக இருக்கக்கூடாது | | |
| `Signup.title` | Create your account | ගිණුමක් සාදන්න | கணக்கை உருவாக்குங்கள் | | |
| `Signup.step` | Step {current} of {total} | පියවර {current} / {total} | படி {current} / {total} | | |
| `Signup.phoneTitle` | Your mobile number | ඔබගේ ජංගම අංකය | உங்கள் கைபேசி எண் | | |
| `Signup.phoneHelp` | We'll text you a 6-digit code to confirm it's yours. | එය ඔබගේ බව තහවුරු කිරීමට අපි ඉලක්කම් 6ක කේතයක් කෙටි පණිවිඩයකින් එවන්නෙමු. | இது உங்களுடையது என உறுதிப்படுத்த 6 இலக்கக் குறியீட்டைக் குறுஞ்செய்தியில் அனுப்புவோம். | | |
| `Signup.phoneLabel` | Mobile number | ජංගම දුරකථන අංකය | கைபேசி எண் | | |
| `Signup.phonePlaceholder` | 07X XXX XXXX | 07X XXX XXXX | 07X XXX XXXX | | |
| `Signup.phoneRequired` | Enter your mobile number. | ඔබගේ ජංගම දුරකථන අංකය ඇතුළත් කරන්න. | உங்கள் கைபேசி எண்ணை உள்ளிடுங்கள். | | |
| `Signup.sendCode` | Send code | කේතය යවන්න | குறியீட்டை அனுப்பு | | |
| `Signup.haveAccount` | Already have an account? | දැනටමත් ගිණුමක් තිබේද? | ஏற்கனவே கணக்கு உள்ளதா? | | |
| `Signup.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `Signup.codeTitle` | Enter the code | කේතය ඇතුළත් කරන්න | குறியீட்டை உள்ளிடுங்கள் | | |
| `Signup.codeSentTo` | We sent a code to {phone}. | අපි {phone} වෙත කේතයක් එවූවෙමු. | {phone} எண்ணுக்குக் குறியீட்டை அனுப்பினோம். | | |
| `Signup.codeLabel` | 6-digit code | ඉලක්කම් 6ක කේතය | 6 இலக்கக் குறியீடு | | |
| `Signup.codeRequired` | Enter all 6 digits of the code. | කේතයේ ඉලක්කම් 6ම ඇතුළත් කරන්න. | குறியீட்டின் 6 இலக்கங்களையும் உள்ளிடுங்கள். | | |
| `Signup.verify` | Verify | තහවුරු කරන්න | உறுதிப்படுத்து | | |
| `Signup.changeNumber` | Change number | අංකය වෙනස් කරන්න | எண்ணை மாற்று | | |
| `Signup.resend` | Send a new code | අලුත් කේතයක් යවන්න | புதிய குறியீட்டை அனுப்பு | | |
| `Signup.resendIn` | New code in {time} | {time} කින් අලුත් කේතයක් | {time} இல் புதிய குறியீடு | | |
| `Signup.detailsTitle` | About you | ඔබ ගැන | உங்களைப் பற்றி | | |
| `Signup.detailsHelp` | Your number is confirmed. Now your name and a password. | ඔබගේ අංකය තහවුරු විය. දැන් ඔබගේ නම සහ මුරපදයක්. | உங்கள் எண் உறுதிப்படுத்தப்பட்டது. இப்போது உங்கள் பெயரும் ஒரு கடவுச்சொல்லும். | | |
| `Signup.fullNameLabel` | Full name | සම්පූර්ණ නම | முழுப் பெயர் | | |
| `Signup.nameRequired` | Enter your name. | ඔබගේ නම ඇතුළත් කරන්න. | உங்கள் பெயரை உள்ளிடுங்கள். | | |
| `Signup.emailLabel` | Email | විද්‍යුත් තැපෑල | மின்னஞ்சல் | | |
| `Signup.emailHelp` | Optional. You can sign in with it too. | අනිවාර්ය නැත. එයින්ද පුරනය විය හැක. | விருப்பத்திற்குரியது. அதைக் கொண்டும் உள்நுழையலாம். | | |
| `Signup.passwordLabel` | Password | මුරපදය | கடவுச்சொல் | | |
| `Signup.passwordRequired` | Choose a password. | මුරපදයක් තෝරන්න. | ஒரு கடவுச்சொல்லைத் தேர்ந்தெடுங்கள். | | |
| `Signup.continue` | Continue | ඉදිරියට | தொடர்க | | |
| `Signup.differentNumber` | Use a different number | වෙනත් අංකයක් භාවිත කරන්න | வேறு எண்ணைப் பயன்படுத்து | | |
| `Signup.consentTitle` | Before you join | එකතු වීමට පෙර | இணைவதற்கு முன் | | |
| `Signup.consentHelp` | Please read and accept these. | කරුණාකර මේවා කියවා පිළිගන්න. | இவற்றைப் படித்து ஏற்றுக்கொள்ளுங்கள். | | |
| `Signup.documentsUnavailable` | We couldn't load the documents. | ලේඛන පූරණය කළ නොහැකි විය. | ஆவணங்களை ஏற்ற முடியவில்லை. | | |
| `Signup.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயல்க | | |
| `Signup.readDocument` | Read the {title} | {title} කියවන්න | {title} படிக்க | | |
| `Signup.version` | Version {version} | අනුවාදය {version} | பதிப்பு {version} | | |
| `Signup.accept` | I have read and accept the {title} | මම {title} කියවා පිළිගනිමි | {title} படித்து ஏற்றுக்கொள்கிறேன் | | |
| `Signup.acceptAll` | Please accept every document to continue. | ඉදිරියට යාමට සෑම ලේඛනයක්ම පිළිගන්න. | தொடர ஒவ்வொரு ஆவணத்தையும் ஏற்றுக்கொள்ளுங்கள். | | |
| `Signup.createAccount` | Create account | ගිණුම සාදන්න | கணக்கை உருவாக்கு | | |
| `Signup.back` | Back | ආපසු | பின்செல் | | |
| `Signup.signInInstead` | Sign in instead | ඒ වෙනුවට පුරනය වන්න | அதற்குப் பதிலாக உள்நுழைக | | |
| `Signup.createdTitle` | Your account is ready | ඔබගේ ගිණුම සූදානම් | உங்கள் கணக்கு தயார் | | |
| `Signup.createdSignIn` | Please sign in with your new account. | කරුණාකර ඔබගේ නව ගිණුමෙන් පුරනය වන්න. | உங்கள் புதிய கணக்கில் உள்நுழையுங்கள். | | |
| `StepUp.title` | Confirm it's you | මෙය ඔබ බව තහවුරු කරන්න | இது நீங்கள்தான் என உறுதிப்படுத்துங்கள் | | |
| `StepUp.description` | This action needs an extra check. We'll text a 6-digit code to the phone number on your account. | මෙම ක්‍රියාවට අමතර පරීක්ෂාවක් අවශ්‍යයි. ඔබගේ ගිණුමේ දුරකථන අංකයට අපි ඉලක්කම් 6ක කේතයක් යවන්නෙමු. | இந்தச் செயலுக்குக் கூடுதல் சரிபார்ப்பு தேவை. உங்கள் கணக்கின் தொலைபேசி எண்ணுக்கு 6 இலக்கக் குறியீட்டை அனுப்புவோம். | | |
| `StepUp.signInFirst` | Please sign in first. | මුලින්ම පුරනය වන්න. | முதலில் உள்நுழையுங்கள். | | |
| `StepUp.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `StepUp.sending` | Sending a code… | කේතයක් යවමින්… | குறியீடு அனுப்பப்படுகிறது… | | |
| `StepUp.sent` | Code sent. It can take a minute to arrive. | කේතය යවන ලදී. එය පැමිණීමට විනාඩියක් පමණ ගත විය හැක. | குறியீடு அனுப்பப்பட்டது. அது வர ஒரு நிமிடம் ஆகலாம். | | |
| `StepUp.codeLabel` | 6-digit code | ඉලක්කම් 6ක කේතය | 6 இலக்கக் குறியீடு | | |
| `StepUp.codeRequired` | Enter all 6 digits of the code. | කේතයේ ඉලක්කම් 6ම ඇතුළත් කරන්න. | குறியீட்டின் 6 இலக்கங்களையும் உள்ளிடுங்கள். | | |
| `StepUp.confirm` | Confirm | තහවුරු කරන්න | உறுதிப்படுத்து | | |
| `StepUp.resend` | Send a new code | අලුත් කේතයක් යවන්න | புதிய குறியீட்டை அனுப்பு | | |
| `StepUp.resendIn` | New code in {time} | {time} කින් අලුත් කේතයක් | {time} இல் புதிய குறியீடு | | |
| `StepUp.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `SelectInstitution.title` | Choose where you're working | ඔබ ක්‍රියා කරන ස්ථානය තෝරන්න | நீங்கள் செயல்படும் இடத்தைத் தேர்ந்தெடுங்கள் | | |
| `SelectInstitution.description` | You belong to more than one place. Pick the one to act for now; you can switch later. | ඔබ ස්ථාන කිහිපයක සාමාජිකයෙකි. දැන් ක්‍රියා කිරීමට එකක් තෝරන්න; පසුව මාරු කළ හැක. | நீங்கள் ஒன்றுக்கு மேற்பட்ட இடங்களில் உறுப்பினர். இப்போது செயல்பட ஒன்றைத் தேர்ந்தெடுங்கள்; பின்னர் மாற்றலாம். | | |
| `SelectInstitution.none` | You aren't a member of any institution. You can still sponsor meals as a donor. | ඔබ කිසිදු ආයතනයක සාමාජිකයෙකු නොවේ. පරිත්‍යාගශීලියෙකු ලෙස ඔබට තවමත් ආහාර වේලවලට අනුග්‍රහය දැක්විය හැක. | நீங்கள் எந்த நிறுவனத்திலும் உறுப்பினர் அல்ல. நன்கொடையாளராக உணவுக்கு அனுசரணை வழங்கலாம். | | |
| `SelectInstitution.signInFirst` | Sign in to choose an institution. | ආයතනයක් තෝරා ගැනීමට මුලින්ම පුරනය වන්න. | நிறுவனத்தைத் தேர்ந்தெடுக்க முதலில் உள்நுழையுங்கள். | | |
| `SelectInstitution.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `SelectInstitution.current` | Current | වත්මන් | தற்போதையது | | |
| `SelectInstitution.goHome` | Go to the home page | මුල් පිටුවට යන්න | முகப்புக்குச் செல் | | |
| `Session.title` | Your session | ඔබගේ සැසිය | உங்கள் அமர்வு | | |
| `Session.signedOut` | You're not signed in. | ඔබ පුරනය වී නැත. | நீங்கள் உள்நுழையவில்லை. | | |
| `Session.unavailable` | Can't reach Sethika right now. Please try again later. | දැන් Sethika වෙත සම්බන්ධ විය නොහැක. කරුණාකර පසුව නැවත උත්සාහ කරන්න. | இப்போது Sethika-வை அணுக முடியவில்லை. பின்னர் மீண்டும் முயலுங்கள். | | |
| `Session.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `Session.signUp` | Create an account | ගිණුමක් සාදන්න | கணக்கை உருவாக்குங்கள் | | |
| `Session.signedIn` | You're signed in. | ඔබ පුරනය වී ඇත. | நீங்கள் உள்நுழைந்துள்ளீர்கள். | | |
| `Session.actingFor` | Acting for {name} | {name} වෙනුවෙන් ක්‍රියා කරයි | {name} சார்பில் செயல்படுகிறீர்கள் | | |
| `Session.switchInstitution` | Switch institution | ආයතනය මාරු කරන්න | நிறுவனத்தை மாற்று | | |
| `Session.signOut` | Sign out | ඉවත් වන්න | வெளியேறு | | |
| `Session.signOutEverywhere` | Sign out on all devices | සියලු උපාංගවලින් ඉවත් වන්න | எல்லாச் சாதனங்களிலிருந்தும் வெளியேறு | | |
| `Session.approved` | Booking approved (demonstration). | වෙන්කිරීම අනුමත විය (නිදර්ශනය). | முன்பதிவு அங்கீகரிக்கப்பட்டது (மாதிரி). | | |
| `Session.approveFailed` | Could not approve the booking. | වෙන්කිරීම අනුමත කළ නොහැකි විය. | முன்பதிவை அங்கீகரிக்க முடியவில்லை. | | |
| `PasswordChange.title` | Change your password | ඔබගේ මුරපදය වෙනස් කරන්න | உங்கள் கடவுச்சொல்லை மாற்றுங்கள் | | |
| `PasswordChange.description` | You'll stay signed in here. Every other device you're signed in on will be signed out. | ඔබ මෙහි පුරනය වී සිටිනු ඇත. ඔබ පුරනය වී ඇති අනෙක් සියලු උපාංගවලින් ඔබව ඉවත් කෙරේ. | இந்தச் சாதனத்தில் நீங்கள் உள்நுழைந்தே இருப்பீர்கள். நீங்கள் உள்நுழைந்துள்ள மற்ற எல்லாச் சாதனங்களிலிருந்தும் வெளியேற்றப்படுவீர்கள். | | |
| `PasswordChange.signInFirst` | Sign in to change your password. | මුරපදය වෙනස් කිරීමට මුලින්ම පුරනය වන්න. | கடவுச்சொல்லை மாற்ற முதலில் உள்நுழையுங்கள். | | |
| `PasswordChange.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `PasswordChange.currentLabel` | Current password | වත්මන් මුරපදය | தற்போதைய கடவுச்சொல் | | |
| `PasswordChange.currentRequired` | Enter your current password. | ඔබගේ වත්මන් මුරපදය ඇතුළත් කරන්න. | உங்கள் தற்போதைய கடவுச்சொல்லை உள்ளிடுங்கள். | | |
| `PasswordChange.currentIncorrect` | That isn't your current password. | එය ඔබගේ වත්මන් මුරපදය නොවේ. | அது உங்கள் தற்போதைய கடவுச்சொல் அல்ல. | | |
| `PasswordChange.nextLabel` | New password | නව මුරපදය | புதிய கடவுச்சொல் | | |
| `PasswordChange.nextRequired` | Choose a new password. | නව මුරපදයක් තෝරන්න. | புதிய கடவுச்சொல்லைத் தேர்ந்தெடுங்கள். | | |
| `PasswordChange.unchanged` | Choose a password that is different from your current one. | ඔබගේ වත්මන් මුරපදයට වඩා වෙනස් මුරපදයක් තෝරන්න. | தற்போதைய கடவுச்சொல்லிலிருந்து வேறுபட்ட ஒன்றைத் தேர்ந்தெடுங்கள். | | |
| `PasswordChange.confirmLabel` | Confirm new password | නව මුරපදය තහවුරු කරන්න | புதிய கடவுச்சொல்லை உறுதிப்படுத்துங்கள் | | |
| `PasswordChange.confirmRequired` | Type your new password again to confirm it. | තහවුරු කිරීමට නව මුරපදය නැවත ඇතුළත් කරන්න. | உறுதிப்படுத்த புதிய கடவுச்சொல்லை மீண்டும் உள்ளிடுங்கள். | | |
| `PasswordChange.confirmMismatch` | This doesn't match the new password. | මෙය නව මුරපදයට නොගැළපේ. | இது புதிய கடவுச்சொல்லுடன் பொருந்தவில்லை. | | |
| `PasswordChange.submit` | Change password | මුරපදය වෙනස් කරන්න | கடவுச்சொல்லை மாற்று | | |
| `PasswordChange.changed` | Password changed | මුරපදය වෙනස් කරන ලදී | கடவுச்சொல் மாற்றப்பட்டது | | |
| `PasswordChange.changedDescription` | Your other devices have been signed out. | ඔබගේ අනෙක් උපාංගවලින් ඔබව ඉවත් කරන ලදී. | உங்கள் மற்ற சாதனங்களிலிருந்து வெளியேற்றப்பட்டீர்கள். | | |
| `PasswordChange.home` | Go to the home page | මුල් පිටුවට යන්න | முகப்புக்குச் செல் | | |
| `Session.changePassword` | Change password | මුරපදය වෙනස් කරන්න | கடவுச்சொல்லை மாற்று | | |
| `Login.forgotPassword` | Forgot password? | මුරපදය අමතකද? | கடவுச்சொல் மறந்துவிட்டதா? | | |
| `Errors.reset_ticket_invalid` | This password reset has expired or was already used. Please verify your number again. | මෙම මුරපදය යළි සැකසීම කල් ඉකුත් වී හෝ දැනටමත් භාවිත කර ඇත. කරුණාකර ඔබගේ අංකය නැවත තහවුරු කරන්න. | இந்தக் கடவுச்சொல் மீட்டமைப்பு காலாவதியாகிவிட்டது அல்லது ஏற்கனவே பயன்படுத்தப்பட்டது. உங்கள் எண்ணை மீண்டும் உறுதிப்படுத்துங்கள். | | |
| `ForgotPassword.title` | Reset your password | ඔබගේ මුරපදය යළි සකසන්න | உங்கள் கடவுச்சொல்லை மீட்டமையுங்கள் | | |
| `ForgotPassword.step` | Step {current} of {total} | පියවර {current} / {total} | படி {current} / {total} | | |
| `ForgotPassword.phoneTitle` | Forgot your password? | ඔබගේ මුරපදය අමතකද? | கடவுச்சொல் மறந்துவிட்டதா? | | |
| `ForgotPassword.phoneHelp` | Enter the mobile number on your account. We'll text it a 6-digit code to set a new password. | ඔබගේ ගිණුමේ ජංගම අංකය ඇතුළත් කරන්න. නව මුරපදයක් සැකසීමට අපි එයට ඉලක්කම් 6ක කේතයක් කෙටි පණිවිඩයකින් එවන්නෙමු. | உங்கள் கணக்கிலுள்ள கைபேசி எண்ணை உள்ளிடுங்கள். புதிய கடவுச்சொல்லை அமைக்க அதற்கு 6 இலக்கக் குறியீட்டைக் குறுஞ்செய்தியில் அனுப்புவோம். | | |
| `ForgotPassword.phoneLabel` | Mobile number | ජංගම දුරකථන අංකය | கைபேசி எண் | | |
| `ForgotPassword.phonePlaceholder` | 07X XXX XXXX | 07X XXX XXXX | 07X XXX XXXX | | |
| `ForgotPassword.phoneRequired` | Enter your mobile number. | ඔබගේ ජංගම දුරකථන අංකය ඇතුළත් කරන්න. | உங்கள் கைபேசி எண்ணை உள்ளிடுங்கள். | | |
| `ForgotPassword.sendCode` | Send code | කේතය යවන්න | குறியீட்டை அனுப்பு | | |
| `ForgotPassword.remembered` | Remembered it? | මතක් වුණාද? | நினைவுக்கு வந்ததா? | | |
| `ForgotPassword.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `ForgotPassword.codeTitle` | Enter the code | කේතය ඇතුළත් කරන්න | குறியீட்டை உள்ளிடுங்கள் | | |
| `ForgotPassword.codeSentTo` | If {phone} belongs to a Sethika account, a code is on its way. It can take a minute to arrive. | {phone} Sethika ගිණුමකට අයත් නම්, කේතයක් එයට එවෙමින් පවතී. එය පැමිණීමට විනාඩියක් පමණ ගත විය හැක. | {phone} ஒரு Sethika கணக்கிற்குரியது என்றால், அதற்குக் குறியீடு அனுப்பப்படுகிறது. அது வர ஒரு நிமிடம் ஆகலாம். | | |
| `ForgotPassword.codeLabel` | 6-digit code | ඉලක්කම් 6ක කේතය | 6 இலக்கக் குறியீடு | | |
| `ForgotPassword.codeRequired` | Enter all 6 digits of the code. | කේතයේ ඉලක්කම් 6ම ඇතුළත් කරන්න. | குறியீட்டின் 6 இலக்கங்களையும் உள்ளிடுங்கள். | | |
| `ForgotPassword.verify` | Verify | තහවුරු කරන්න | உறுதிப்படுத்து | | |
| `ForgotPassword.changeNumber` | Change number | අංකය වෙනස් කරන්න | எண்ணை மாற்று | | |
| `ForgotPassword.resend` | Send a new code | අලුත් කේතයක් යවන්න | புதிய குறியீட்டை அனுப்பு | | |
| `ForgotPassword.resendIn` | New code in {time} | {time} කින් අලුත් කේතයක් | {time} இல் புதிய குறியீடு | | |
| `ForgotPassword.passwordTitle` | Choose a new password | නව මුරපදයක් තෝරන්න | புதிய கடவுச்சொல்லைத் தேர்ந்தெடுங்கள் | | |
| `ForgotPassword.passwordHelp` | Every device signed in to your account will be signed out. You will sign in again with the new password. | ඔබගේ ගිණුමට පුරනය වී ඇති සියලු උපාංගවලින් ඉවත් කෙරේ. ඔබ නව මුරපදයෙන් නැවත පුරනය වනු ඇත. | உங்கள் கணக்கில் உள்நுழைந்துள்ள எல்லாச் சாதனங்களிலிருந்தும் வெளியேற்றப்படுவீர்கள். புதிய கடவுச்சொல்லுடன் மீண்டும் உள்நுழைவீர்கள். | | |
| `ForgotPassword.passwordLabel` | New password | නව මුරපදය | புதிய கடவுச்சொல் | | |
| `ForgotPassword.passwordRequired` | Choose a new password. | නව මුරපදයක් තෝරන්න. | புதிய கடவுச்சொல்லைத் தேர்ந்தெடுங்கள். | | |
| `ForgotPassword.confirmLabel` | Type the new password again | නව මුරපදය නැවත ඇතුළත් කරන්න | புதிய கடவுச்சொல்லை மீண்டும் உள்ளிடுங்கள் | | |
| `ForgotPassword.confirmRequired` | Type the new password again. | නව මුරපදය නැවත ඇතුළත් කරන්න. | புதிய கடவுச்சொல்லை மீண்டும் உள்ளிடுங்கள். | | |
| `ForgotPassword.confirmMismatch` | The two passwords don't match. | මුරපද දෙක එකිනෙකට නොගැළපේ. | இரண்டு கடவுச்சொற்களும் பொருந்தவில்லை. | | |
| `ForgotPassword.submit` | Change password | මුරපදය වෙනස් කරන්න | கடவுச்சொல்லை மாற்று | | |
| `ForgotPassword.startAgain` | Start again | මුල සිට පටන් ගන්න | மீண்டும் தொடங்கு | | |
| `ForgotPassword.changedTitle` | Your password was changed | ඔබගේ මුරපදය වෙනස් කරන ලදී | உங்கள் கடவுச்சொல் மாற்றப்பட்டது | | |
| `ForgotPassword.changedDescription` | Sign in with your new password. | ඔබගේ නව මුරපදයෙන් පුරනය වන්න. | உங்கள் புதிய கடவுச்சொல்லுடன் உள்நுழையுங்கள். | | |

### Sign-in by code (`/login/otp`)

Rows for the sign-in-by-code screen. `LoginOtp.codeSentTo` is worded "if {phone} is on a Sethika
account" on purpose: the API answers every number the same so that nobody can learn which numbers
have accounts, and the page must not undo that by saying the code was sent. Please keep that
conditional meaning in the translation, even if it reads a little indirect. The rows that repeat
`Signup.*` text reuse its drafts word for word.

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `Login.useCode` | Sign in with a code instead | ඒ වෙනුවට කේතයකින් පුරනය වන්න | பதிலாகக் குறியீட்டின் மூலம் உள்நுழைக | | |
| `LoginOtp.title` | Sign in with a code | කේතයකින් පුරනය වන්න | குறியீட்டின் மூலம் உள்நுழைக | | |
| `LoginOtp.phoneHelp` | Enter the mobile number on your Sethika account. We'll text it a 6-digit code. | ඔබගේ Sethika ගිණුමේ ජංගම අංකය ඇතුළත් කරන්න. අපි එයට ඉලක්කම් 6ක කේතයක් කෙටි පණිවිඩයකින් යවන්නෙමු. | உங்கள் Sethika கணக்கில் உள்ள கைபேசி எண்ணை உள்ளிடுங்கள். அதற்கு 6 இலக்கக் குறியீட்டைக் குறுஞ்செய்தியாக அனுப்புவோம். | | |
| `LoginOtp.phoneLabel` | Mobile number | ජංගම දුරකථන අංකය | கைபேசி எண் | | |
| `LoginOtp.phonePlaceholder` | 07X XXX XXXX | 07X XXX XXXX | 07X XXX XXXX | | |
| `LoginOtp.phoneRequired` | Enter your mobile number. | ඔබගේ ජංගම දුරකථන අංකය ඇතුළත් කරන්න. | உங்கள் கைபேசி எண்ணை உள்ளிடுங்கள். | | |
| `LoginOtp.sendCode` | Send code | කේතය යවන්න | குறியீட்டை அனுப்பு | | |
| `LoginOtp.usePassword` | Sign in with a password instead | ඒ වෙනුවට මුරපදයකින් පුරනය වන්න | பதிலாகக் கடவுச்சொல் மூலம் உள்நுழைக | | |
| `LoginOtp.noAccount` | New to Sethika? | Sethika වෙත අලුත්ද? | Sethika-வுக்குப் புதியவரா? | | |
| `LoginOtp.createAccount` | Create an account | ගිණුමක් සාදන්න | கணக்கை உருவாக்குங்கள் | | |
| `LoginOtp.codeTitle` | Enter the code | කේතය ඇතුළත් කරන්න | குறியீட்டை உள்ளிடுங்கள் | | |
| `LoginOtp.codeSentTo` | If {phone} is on a Sethika account, a code is on its way. It can take a minute to arrive. | {phone} Sethika ගිණුමක ඇත්නම්, කේතයක් පැමිණෙමින් පවතී. එය පැමිණීමට විනාඩියක් පමණ ගත විය හැක. | {phone} ஒரு Sethika கணக்கில் இருந்தால், குறியீடு வந்துகொண்டிருக்கிறது. அது வர ஒரு நிமிடம் ஆகலாம். | | |
| `LoginOtp.codeLabel` | 6-digit code | ඉලක්කම් 6ක කේතය | 6 இலக்கக் குறியீடு | | |
| `LoginOtp.codeRequired` | Enter all 6 digits of the code. | කේතයේ ඉලක්කම් 6ම ඇතුළත් කරන්න. | குறியீட்டின் 6 இலக்கங்களையும் உள்ளிடுங்கள். | | |
| `LoginOtp.signIn` | Sign in | පුරනය වන්න | உள்நுழைக | | |
| `LoginOtp.changeNumber` | Change number | අංකය වෙනස් කරන්න | எண்ணை மாற்று | | |
| `LoginOtp.resend` | Send a new code | අලුත් කේතයක් යවන්න | புதிய குறியீட்டை அனுப்பு | | |
| `LoginOtp.resendIn` | New code in {time} | {time} කින් අලුත් කේතයක් | {time} இல் புதிய குறியீடு | | |
| `Errors.otp_invalid` | That code didn't work. Check the number and the code, or send a new code. | එම කේතය වැඩ කළේ නැත. අංකය සහ කේතය පරීක්ෂා කරන්න, නැතහොත් අලුත් කේතයක් යවන්න. | அந்தக் குறியீடு வேலை செய்யவில்லை. எண்ணையும் குறியீட்டையும் சரிபாருங்கள், அல்லது புதிய குறியீட்டை அனுப்புங்கள். | | |

## SMS templates (`notify.Templates`)

These are not in the JSON files. They are seeded by
`src/Sethika.Database/Scripts/Seed/11_NotifyTemplates.sql`, and a correction goes there (the seed
updates the body in place on the next publish). The key column is `Code / Language`.

Each message must stay inside **one SMS segment**, because every extra segment is paid for.
Sinhala and Tamil are sent as UCS-2, which allows **70 characters** per segment; English is GSM-7,
160. `{code}` is replaced by the six-digit code and must stay in the text exactly as written. The
counts below are with the code in place.

Questions for the reviewers:

1. The signup and step-up messages are the same text today, carried over from the code they
   replaced. Should the step-up message say that it confirms a sensitive action (a warning against
   phishing), if that still fits in 70 characters?
2. `සත්‍යාපන කේතය` and `சரிபார்ப்புக் குறியீடு` for "verification code": the words people expect
   from bank and telecom OTPs?
3. The sign-in SMS says `පිවිසුම්` ("entry/login") where the web pages say `පුරනය වන්න` for "sign
   in". Which one would a donor recognise in a text message?
4. The Tamil sign-in SMS is 73 characters with the code, three over one segment, so it is sent and
   paid for as two. Can it be shortened without losing "do not share it"?

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `OTP_SIGNUP / SMS` | Your Sethika verification code is {code} (40) | ඔබගේ Sethika සත්‍යාපන කේතය: {code} (34) | உங்கள் Sethika சரிபார்ப்புக் குறியீடு: {code} (45) | | |
| `OTP_STEPUP / SMS` | Your Sethika verification code is {code} (40) | ඔබගේ Sethika සත්‍යාපන කේතය: {code} (34) | உங்கள் Sethika சரிபார்ப்புக் குறியீடு: {code} (45) | | |
| `OTP_RESET / SMS` | Your Sethika password reset code is {code} (42) | ඔබගේ Sethika මුරපදය යළි සැකසීමේ කේතය: {code} (44) | உங்கள் Sethika கடவுச்சொல் மீட்டமைப்புக் குறியீடு: {code} (56) | | |
| `OTP_LOGIN / SMS` | Your Sethika sign-in code is {code}. Do not share it with anyone. (65) | ඔබගේ Sethika පිවිසුම් කේතය: {code}. එය කිසිවෙකු සමඟ බෙදා නොගන්න. (64) | உங்கள் Sethika உள்நுழைவுக் குறியீடு: {code}. இதை யாருடனும் பகிர வேண்டாம். (73) | | |

### Onboarding texts

Sent to an institution's applicant at four points of an application. `{requestNo}` is replaced by
a 15-character number such as `REQ-2026-000123` and must stay exactly as written; the counts are
with it in place. These are allowed **two** segments in Sinhala and Tamil (at most four go to one
application), so the limit is 134 characters, but one segment (70) is better where the meaning
survives.

Questions for the reviewers:

1. `අයදුම්පත` / `விண்ணப்பம்` for "application": the word an institution's secretary would use?
2. The texts say only "sign in", not where. Is that clear enough, or should they name sethika.com?
3. `අනුමත නොවීය` and `அங்கீகரிக்கப்படவில்லை` for "not approved": too blunt for a temple or a
   children's home? The reason is shown after signing in, not in the text.

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `ONBOARD_SUBMITTED / SMS` | Sethika: we received application {requestNo}. We will text you when it has been reviewed. (93) | Sethika: අයදුම්පත {requestNo} ලැබුණා. සමාලෝචනයෙන් පසු දන්වන්නෙමු. (69) | Sethika: விண்ணப்பம் {requestNo} கிடைத்தது. மதிப்பாய்வின் பின் அறிவிப்போம். (78) | | |
| `ONBOARD_INFO_REQUESTED / SMS` | Sethika: the reviewer has a question about application {requestNo}. Sign in to answer it. (93) | Sethika: අයදුම්පත {requestNo} ගැන ප්‍රශ්නයක් ඇත. පිළිතුරු දීමට පිවිසෙන්න. (77) | Sethika: விண்ணப்பம் {requestNo} பற்றி ஒரு கேள்வி உள்ளது. பதிலளிக்க உள்நுழையவும். (84) | | |
| `ONBOARD_APPROVED / SMS` | Sethika: application {requestNo} is approved. Sign in to open your institution. (83) | Sethika: අයදුම්පත {requestNo} අනුමතයි. ආයතනය විවෘත කිරීමට පිවිසෙන්න. (72) | Sethika: விண்ணப்பம் {requestNo} அங்கீகரிக்கப்பட்டது. நிறுவனத்தைத் திறக்க உள்நுழையவும். (90) | | |
| `ONBOARD_REJECTED / SMS` | Sethika: application {requestNo} was not approved. Sign in to see why. (74) | Sethika: අයදුම්පත {requestNo} අනුමත නොවීය. හේතුව බැලීමට පිවිසෙන්න. (70) | Sethika: விண்ணப்பம் {requestNo} அங்கீகரிக்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும். (91) | | |

### Booking texts

Sent to a donor about one meal booking. Every text starts with the same line,
`Sethika {bookingNo}: {institution}, {date} {time}.`, and the sentence after it is what needs
review. The placeholders are kept out of the sentences on purpose, so no name has to take a
Sinhala or Tamil case ending. `{time}` is when the meal starts; the slot's own name is left out
because the institution types it freely, and nothing typed freely is sent from the Sethika sender id. The counts are with `BK-2026-000123`, a 20-character institution
name, `2026-11-14` and `11:00` in place. Two segments (134) are allowed; one is not reachable with
the opening line.

Questions for the reviewers:

1. `වෙන්කිරීම` / `முன்பதிவு` for "booking", and `ඉල්ලීම` / `கோரிக்கை` for "request": do donors
   use these words for reserving a meal at a temple, kovil, mosque, church or home?
2. `පිළිගත්තා` / `ஏற்கப்பட்டது` ("accepted") rather than `අනුමත` ("approved"): is it warmer, and is
   it clear that the meal is now theirs?
3. `ආයතනය` / `நிறுவனம்` ("the institution") in the cancellation: would a donor say the temple's
   or home's own kind of name instead? The text cannot know which one it is.
4. The reminder is sent at 18:00 the evening before. Is `හෙටයි` / `நாளை` enough, or should it
   name the time of the meal?

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `BOOKING_REQUESTED / SMS` | Your request was sent. We will text you when they reply. (120) | ඔබගේ ඉල්ලීම යැවුණා. පිළිතුර ලැබුණු විට දන්වන්නෙමු. (114) | உங்கள் கோரிக்கை அனுப்பப்பட்டது. பதில் வந்ததும் அறிவிப்போம். (123) | | |
| `BOOKING_CONFIRMED / SMS` | Your booking is confirmed. Thank you. (101) | ඔබගේ වෙන්කිරීම තහවුරුයි. ස්තූතියි. (98) | உங்கள் முன்பதிவு உறுதியானது. நன்றி. (99) | | |
| `BOOKING_APPROVED / SMS` | Your request was accepted. Thank you. (101) | ඔබගේ ඉල්ලීම පිළිගත්තා. ස්තූතියි. (96) | உங்கள் கோரிக்கை ஏற்கப்பட்டது. நன்றி. (100) | | |
| `BOOKING_REJECTED / SMS` | Your request was not accepted. Sign in to see why. (114) | ඔබගේ ඉල්ලීම පිළිගත්තේ නැත. හේතුව බැලීමට පිවිසෙන්න. (114) | உங்கள் கோரிக்கை ஏற்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும். (128) | | |
| `BOOKING_CANCELLED / SMS` | The institution cancelled this booking. Sign in to see why. (123) | ආයතනය මෙම වෙන්කිරීම අවලංගු කළා. හේතුව බැලීමට පිවිසෙන්න. (119) | நிறுவனம் இதை ரத்து செய்தது. காரணத்தைப் பார்க்க உள்நுழையவும். (124) | | |
| `BOOKING_REMINDER / SMS` | A reminder: this booking is tomorrow. (101) | මතක් කිරීමක්: මෙම වෙන්කිරීම හෙටයි. (98) | நினைவூட்டல்: இந்த முன்பதிவு நாளை. (97) | | |

### Religious service texts (Phase E, 8.8)

Sent about one service appointment, to the donor who asked for it and to each religious leader
assigned. Every text opens with `Sethika {appointmentNo}: {institution}, {date} {time}.`, as the
booking texts do; only the sentence after it needs review. `{time}` is when the service starts in
Colombo. The service's own name is left out because the institution types it freely.

Questions for the reviewers:

1. `සේවා ඉල්ලීම` / `சேவைக் கோரிக்கை` for "service request" (a pirith, a sermon, a pooja, a blessing,
   a funeral rite): is "service" the word a donor would use, or would they name it as an
   invitation (`ආරාධනාව`)?
2. A leader may be a Buddhist monk, a Catholic or Anglican priest, a Hindu kurukkal or a Muslim
   moulavi, and the text cannot know which. The Sinhala leader texts therefore use **no pronoun**
   (`පවරා ඇත`, not `ඔබ` / `ඔබවහන්සේ` / `ඔබතුමා`). Is that respectful enough for all four, or does it
   read as curt? The Tamil ones use the respectful `தாங்கள்`: is that right for each faith?
3. `පවරා ඇත` / `நியமிக்கப்பட்டுள்ளீர்கள்` ("assigned"): is there a warmer word for asking clergy to
   come, without sounding like an order?
4. `පැමිණීම අවශ්‍ය නැත` / `வர வேண்டியதில்லை` ("no need to come") after a cancellation: polite enough?

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `APPOINTMENT_REQUESTED / SMS` | Your service request was sent. We will text you when they reply. | ඔබගේ සේවා ඉල්ලීම යැවුණා. පිළිතුර ලැබුණු විට දන්වන්නෙමු. | உங்கள் சேவைக் கோரிக்கை அனுப்பப்பட்டது. பதில் வந்ததும் அறிவிப்போம். | | |
| `APPOINTMENT_APPROVED / SMS` | Your service request was accepted. Thank you. | ඔබගේ සේවා ඉල්ලීම පිළිගත්තා. ස්තූතියි. | உங்கள் சேவைக் கோரிக்கை ஏற்கப்பட்டது. நன்றி. | | |
| `APPOINTMENT_REJECTED / SMS` | Your service request was not accepted. Sign in to see why. | ඔබගේ සේවා ඉල්ලීම පිළිගත්තේ නැත. හේතුව බැලීමට පිවිසෙන්න. | உங்கள் சேவைக் கோரிக்கை ஏற்கப்படவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும். | | |
| `APPOINTMENT_CANCELLED / SMS` | The institution cancelled this service. Sign in to see why. | ආයතනය මෙම සේවාව අවලංගු කළා. හේතුව බැලීමට පිවිසෙන්න. | நிறுவனம் இந்தச் சேவையை ரத்து செய்தது. காரணத்தைப் பார்க்க உள்நுழையவும். | | |
| `APPOINTMENT_REMINDER / SMS` | A reminder: this service is tomorrow. | මතක් කිරීමක්: මෙම සේවාව හෙටයි. | நினைவூட்டல்: இந்தச் சேவை நாளை. | | |
| `LEADER_ASSIGNED / SMS` | You are assigned to this service. If you cannot come, please tell the institution. | මෙම සේවාව සඳහා පවරා ඇත. පැමිණිය නොහැකි නම් කරුණාකර ආයතනයට දන්වන්න. | இந்தச் சேவைக்குத் தாங்கள் நியமிக்கப்பட்டுள்ளீர்கள். வர இயலாவிட்டால் தயவுசெய்து நிறுவனத்திடம் தெரிவிக்கவும். | | |
| `LEADER_REMOVED / SMS` | You are no longer assigned to this service. | මෙම සේවාවේ පැවරීම ඉවත් කළා. | இந்தச் சேவைக்கான தங்கள் நியமனம் நீக்கப்பட்டது. | | |
| `LEADER_CANCELLED / SMS` | This service was cancelled; there is no need to come. | මෙම සේවාව අවලංගු කළා. පැමිණීම අවශ්‍ය නැත. | இந்தச் சேவை ரத்து செய்யப்பட்டது. தாங்கள் வர வேண்டியதில்லை. | | |
| `LEADER_REMINDER / SMS` | A reminder: you are assigned to this service tomorrow. | මතක් කිරීමක්: පවරා ඇති මෙම සේවාව හෙටයි. | நினைவூட்டல்: தாங்கள் நியமிக்கப்பட்ட இந்தச் சேவை நாளை. | | |

## Institution application screens (`Apply` namespace)

The applicant's screens (plan 02, 5.5): `web/messages/si.json` and `ta.json`, namespace `Apply`,
169 strings, and `Home.apply`. Every Sinhala and Tamil string there is a draft. They are not copied
into a table here because of their number: review them in the JSON files side by side with
`en.json`, and record sign-off below.

Questions for the reviewers:

1. `නිලධාරීන්` / `பதவியாளர்கள்` for "office bearers": what a temple society or a children's home
   committee calls its office bearers?
2. `ජා.හැ. අංකය` / `தே.அ.அ. எண்` for "NIC number": the abbreviation people write on forms?
3. `ග්‍රාම නිලධාරී වසම` / `கிராம உத்தியோகத்தர் பிரிவு` for "Grama Niladhari division".
4. `සමාලෝචකයා` / `மதிப்பாய்வாளர்` for "reviewer": would "Sethika team" read more naturally?
5. `ප්‍රකාශය` / `உறுதிமொழி` for the "declaration" an institution makes when it applies.

| Namespace | Reviewer (si) | Reviewer (ta) | Status |
|---|---|---|---|
| `Apply`, `Home.apply` | | | |

## Reviewer screens (`Review` namespace)

The platform reviewer's and administrator's screens (plan 02, 5.6): namespace `Review`, 116
strings, and `Home.review`; field labels are borrowed from `Apply`, so each has one translation.
Drafts, as above. `Apply.institutionSession` was rewritten at the same time (an institution's owner
cannot switch back to their own account yet) and needs a second look.

Questions for the reviewers:

1. `සමාලෝචනය` / `மதிப்பாய்வு` for "review" as a staff task, beside its use for the applicant.
2. `පිරික්සුම් ලැයිස්තුව` / `சரிபார்ப்புப் பட்டியல்` for the verification "checklist".
3. "Do not accept" a document (`පිළිනොගන්න` / `ஏற்க வேண்டாம்`) and "Do not approve" an application
   (`අනුමත නොකරන්න` / `அங்கீகரிக்க வேண்டாம்`): firm enough, and not rude to a temple?
4. `වේදිකා පරිපාලක` / `தள நிர்வாகி` for "platform administrator".

| Namespace | Reviewer (si) | Reviewer (ta) | Status |
|---|---|---|---|
| `Review`, `Home.review`, `Apply.institutionSession` | | | |

## Mobile app (`mobile/src/i18n/messages`, `App` namespace)

The mobile app copies its shared sections (`Brand`, `Login`, `LoginOtp`, `Common`,
`LanguageSwitcher`, `Errors`, `Password`, `Signup`, and four `Session` keys) from `web/messages`, so
those rows above cover both. A correction there is copied into `mobile/src/i18n/messages` in the
same commit. Only the `App.*` keys below are the app's own.

Question for the reviewers: the Explore title says "a place to give" (`දායක විය හැකි ස්ථානයක්`,
`பங்களிக்க ஓர் இடம்`) to stay faith-neutral. Does it read as a meal *and* a donation, not only money?

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `App.tabs.explore` | Explore | සොයන්න | தேடு | | |
| `App.tabs.bookings` | My bookings | මගේ වෙන්කිරීම් | எனது முன்பதிவுகள் | | |
| `App.tabs.profile` | Profile | පැතිකඩ | சுயவிவரம் | | |
| `App.explore.title` | Find a place to give | දායක විය හැකි ස්ථානයක් සොයන්න | பங்களிக்க ஓர் இடத்தைத் தேடுங்கள் | | |
| `App.explore.comingSoon` | Temples, kovils, mosques, churches and care homes will appear here soon. | පන්සල්, කෝවිල්, මුස්ලිම් පල්ලි, කිතුනු දේවස්ථාන සහ සත්කාර නිවාස ඉක්මනින් මෙහි පෙන්වනු ඇත. | விகாரைகள், கோவில்கள், பள்ளிவாசல்கள், தேவாலயங்கள் மற்றும் பராமரிப்பு இல்லங்கள் விரைவில் இங்கே தோன்றும். | | |
| `App.bookings.title` | My bookings | මගේ වෙන්කිරීම් | எனது முன்பதிவுகள் | | |
| `App.bookings.comingSoon` | Your bookings will appear here once booking opens. | වෙන්කිරීම් ආරම්භ වූ පසු ඔබගේ වෙන්කිරීම් මෙහි පෙන්වනු ඇත. | முன்பதிவு தொடங்கியதும் உங்கள் முன்பதிவுகள் இங்கே தோன்றும். | | |
| `App.profile.title` | Profile | පැතිකඩ | சுயவிவரம் | | |
| `App.profile.greeting` | Hello, {name} | ආයුබෝවන්, {name} | வணக்கம், {name} | | |
| `App.profile.signOutEverywhereHelp` | Ends your session on this phone and in every browser where you are signed in. | මෙම දුරකථනයේ සහ ඔබ පිවිසී ඇති සෑම බ්‍රව්සරයකම ඔබගේ සැසිය අවසන් කරයි. | இந்தத் தொலைபேசியிலும் நீங்கள் உள்நுழைந்துள்ள எல்லா உலாவிகளிலும் உங்கள் அமர்வை முடிக்கும். | | |

## Legal texts (`core.LegalDocuments`)

Seeded by `src/Sethika.Database/Scripts/Seed/09_LegalDocuments.sql`. These are `0.1-draft`
placeholders, and their English is a placeholder too: the real legal text has not been written.
Review the Sinhala and Tamil for meaning and tone only.

A seeded version is **never edited in place**. Its `ContentSha256` is the proof of what a user
accepted, so a correction is a new `Version` row with `IsCurrent = 1`, not a change to the old
text.

| Code / Language | What it is | Reviewer | Status |
|---|---|---|---|
| `TERMS / si`, `TERMS / ta` | Terms of Use placeholder, accepted at signup | | |
| `PRIVACY / si`, `PRIVACY / ta` | Privacy Policy placeholder, accepted at signup | | |
| `INSTITUTION_DECLARATION / si`, `INSTITUTION_DECLARATION / ta` | What an applicant declares when submitting an institution application (plan 02): the details and documents are true, the institution authorised them to apply, and they have seen this draft | | |

Question for the reviewers: the declaration says `ආයතනය ඔබට බලය දී ඇති බව` / `நிறுவனம் உங்களுக்கு
அதிகாரம் அளித்துள்ளது` for "the institution has authorised you". Is that how a trustee or a home's
manager would put it?

## Booking screens (`Booking`, `Discover`, `MyBookings`, `Slots`, `Calendar` namespaces)

The donor's and the institution's booking screens (plan 03, 5.7): about 290 strings in five
namespaces, and four `Home` links (`find`, `mine`, `calendar`, `slots`). Drafts, as above: review
them in the JSON files side by side with `en.json`. The words are faith-neutral on purpose: "meal"
and "sponsor", never one religion's word for an offering.

Questions for the reviewers:

1. `අනුග්‍රාහකයා` / `அனுசரணையாளர்` for "sponsor" and `පරිත්‍යාගශීලියා` / `நன்கொடையாளர்` for
   "donor": two words where English uses them almost alike. One word for both reads better?
2. `ආහාර වේල` / `உணவு வேளை` for a "meal" an institution offers (Lunch, Evening meal).
3. `සිහිවීම පිණිස` / `நினைவாக` for "in memory of": right for every faith's remembrance?
4. `පාත්‍රය` / `கிண்ணம்` for the "bowl" that fills on the calendar.
5. `සපයා අවසන්` / `வழங்கப்பட்டது` ("served") and `සිදු නොවීය` / `நடைபெறவில்லை` ("did not happen")
   for a meal's outcome.

| Namespace | Reviewer (si) | Reviewer (ta) | Status |
|---|---|---|---|
| `Booking`, `Discover`, `MyBookings`, `Slots`, `Calendar`, `Home.find/mine/calendar/slots` | | | |

## Phase F: mobile booking screens (`mobile/src/i18n/messages`, slices F.3 and F.4)

The app's Explore, institution page, booking sheet and My bookings use the web's `Booking`,
`Discover` and `MyBookings` sections, **copied unchanged** from `web/messages` (the "Booking
screens" row above covers them for both). Two `App.*` placeholders are gone: `App.explore.comingSoon`
and `App.bookings.comingSoon`. The keys below are the app's own and are drafts.

`App.dates.*` are not sentences but the **order** of a date's parts: the app never asks the phone's
`Intl` for names (Hermes takes them from the phone's own ICU, which differs between phones), so
the names come from `Booking.dates` and the order from these. The Sinhala order is the web's
(`2026 නොවැම්බර් 1, ඉරිදා`); the Tamil one is CLDR's (`ஞாயிறு, 1 நவம்பர், 2026`).

Question for the reviewers: `තට්ටු කරන්න` / `தட்டவும்` for "tap" a day on the screen. Is that what
people say, or is there a more usual word?

| Key | English (source) | Sinhala (draft) | Tamil (draft) | Reviewer | Status |
|---|---|---|---|---|---|
| `App.dates.monthYear` | {month} {year} | {year} {month} | {month} {year} | | |
| `App.dates.full` | {weekday}, {day} {month} {year} | {year} {month} {day}, {weekday} | {weekday}, {day} {month}, {year} | | |
| `App.dates.dayMonth` | {weekday}, {day} {month} | {weekday}, {month} {day} | {weekday}, {day} {month} | | |
| `App.calendar.dayStatus` | {date}: {status} | {date}: {status} | {date}: {status} | | |
| `App.calendar.tapHint` | Tap an open day to book it. Tap any other day to see why it is not open. | වෙන් කිරීමට විවෘත දිනයක් තට්ටු කරන්න. වෙනත් දිනයක් විවෘත නැත්තේ ඇයිදැයි දැනගැනීමට එය තට්ටු කරන්න. | முன்பதிவு செய்யத் திறந்துள்ள நாளைத் தட்டவும். மற்ற நாள் ஏன் திறக்கப்படவில்லை என்பதைப் பார்க்க அதைத் தட்டவும். | | |
| `App.profile.signedOut` | Sign in to book a meal and see your bookings. | ආහාර වේලක් වෙන් කිරීමට සහ ඔබගේ වෙන්කිරීම් බැලීමට පුරනය වන්න. | உணவு வேளையை முன்பதிவு செய்யவும் உங்கள் முன்பதிவுகளைப் பார்க்கவும் உள்நுழையுங்கள். | | |
| `App.explore.greeting.morning` | Good morning | සුබ උදෑසනක් | காலை வணக்கம் | | |
| `App.explore.greeting.afternoon` | Good afternoon | සුබ දහවලක් | மதிய வணக்கம் | | |
| `App.explore.greeting.evening` | Good evening | සුබ සන්ධ්‍යාවක් | மாலை வணக்கம் | | |
| `App.explore.voice` | One meal can brighten someone's whole day. | එක් ආහාර වේලකින් කෙනෙකුගේ මුළු දවසම එළිය කළ හැකිය. | ஒரு வேளை உணவு ஒருவரின் முழு நாளையும் ஒளிரச் செய்யும். | | |
| `App.explore.kinds` | Kind of place | ස්ථාන වර්ගය | இட வகை | | |
| `App.explore.allKinds` | All | සියල්ල | அனைத்தும் | | |
| `App.explore.count` | {count, plural, one {# place} other {# places}} | {count, plural, one {ස්ථානයක්} other {ස්ථාන #}} | {count, plural, one {# இடம்} other {# இடங்கள்}} | | |
| `App.institution.directions` | Directions | මාර්ග උපදෙස් | வழிகாட்டல் | | |
| `App.profile.thanks` | Thank you for every meal you share. | ඔබ බෙදාගන්නා සෑම ආහාර වේලකටම ස්තූතියි. | நீங்கள் பகிரும் ஒவ்வொரு உணவுக்கும் நன்றி. | | |
| `App.profile.stats.upcoming` | Coming up | ඉදිරියට | வரவிருப்பவை | | |
| `App.profile.stats.given` | Meals given | දුන් ආහාර වේල් | வழங்கிய உணவுகள் | | |
| `App.profile.stats.fed` | People fed | ආහාර ලැබූ අය | உணவு பெற்றோர் | | |
| `App.profile.next` | Your next meal | ඔබගේ ඊළඟ ආහාර වේල | உங்கள் அடுத்த உணவு வேளை | | |
| `App.profile.preferences` | Preferences | මනාපයන් | விருப்பத்தேர்வுகள் | | |
| `App.profile.account` | Account | ගිණුම | கணக்கு | | |
| `App.profile.appearance` | Appearance | පෙනුම | தோற்றம் | | |
| `App.profile.theme.auto` | Auto | ස්වයංක්‍රීය | தானியங்கு | | |
| `App.profile.theme.light` | Light | ආලෝක | வெளிர் | | |
| `App.profile.theme.dark` | Dark | අඳුරු | இருண்ட | | |
| `App.profile.joinTitle` | Give a meal, from wherever you are | ඔබ කොහේ සිටියත්, ආහාර වේලක් දෙන්න | நீங்கள் எங்கிருந்தாலும், ஒரு வேளை உணவு வழங்குங்கள் | | |
| `App.profile.benefits.find` | Find places across Sri Lanka that welcome meal sponsors. | ආහාර අනුග්‍රාහකයන් පිළිගන්නා ස්ථාන ශ්‍රී ලංකාව පුරා සොයන්න. | உணவு அனுசரணையாளர்களை வரவேற்கும் இடங்களை இலங்கை முழுவதும் தேடுங்கள். | | |
| `App.profile.benefits.book` | Book a day in a few taps, and see every booking in one place. | තට්ටු කිහිපයකින් දිනයක් වෙන් කර, සෑම වෙන්කිරීමක්ම එක තැනකින් බලන්න. | சில தட்டல்களில் ஒரு நாளை முன்பதிவு செய்து, ஒவ்வொரு முன்பதிவையும் ஒரே இடத்தில் பாருங்கள். | | |
| `App.profile.benefits.remind` | Get texts and reminders in your own language. | ඔබේම භාෂාවෙන් කෙටි පණිවිඩ සහ සිහිකැඳවීම් ලබාගන්න. | உங்கள் சொந்த மொழியில் குறுஞ்செய்திகளும் நினைவூட்டல்களும் பெறுங்கள். | | |
| `App.profile.version` | Version {version} | අනුවාදය {version} | பதிப்பு {version} | | |

## App shell, dashboard and access screens (plan 04: `Shell`, `Dashboard`, `Access` namespaces)

The web app's frame (menu, account menu, who the session acts as), the landing page and the
dashboard, and the access screens (roles, the permission matrix, staff, invitations): about 250
strings, plus `Common.switchToPersonal`, a new `Booking.gate.institutionSession` and
`Apply.institutionSession` (the old texts said switching back was impossible; it now is). The
`Home` and `PermissionDebug` namespaces were removed with the old home page: their rows above can
go. Drafts, as above: review them in the JSON files side by side with `en.json`.

Questions for the reviewers:

1. `පරිත්‍යාග` / `நன்கொடை` as the menu section for a donor's own things ("Giving"). Natural as a
   heading, or is there a warmer word?
2. `භූමිකාව` / `பங்கு` for a "role" (Owner, Manager, a custom "Kitchen helper"), and
   `අවසර` / `அனுமதி` for a "permission". Do staff of a temple or a care home use these words?
3. `කාර්ය මණ්ඩලය` / `பணியாளர்கள்` for "staff", which here includes volunteers and religious
   leaders who are not employees.
4. The greeting `සුබ උදෑසනක්` / `காலை வணக்கம்` and the landing page's eyebrow
   `ආහාර වේලක් දෙන්න, සතුට බෙදාගන්න` / `ஒரு வேளை உணவு, ஒரு ஆசீர்வாதம்`: faith-neutral enough?
5. The action names on the role editor's pills (`Access.actions`: View, Add, Change, Approve,
   Reject): short imperatives, as English. Natural on a button-like pill?

| Namespace | Reviewer (si) | Reviewer (ta) | Status |
|---|---|---|---|
| `Shell`, `Dashboard`, `Access`, `Common.switchToPersonal`, `Booking.gate.institutionSession`, `Apply.institutionSession` | | | |

## Role edits and owners (2026-10-31, `Access.errors`)

Three refusals the access screens can now show: a role saved by someone else while you edited it,
an edit sent without the role's version, and taking away an institution's only owner.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Access.errors.changed_meanwhile` | Someone else changed this role while you were editing it. Reload the page to see their change, then make yours again. | ඔබ සංස්කරණය කරන අතරතුර වෙනත් අයෙක් මෙම භූමිකාව වෙනස් කළා. ඔවුන්ගේ වෙනස බැලීමට පිටුව නැවත පූරණය කර, ඔබේ වෙනස නැවත කරන්න. | நீங்கள் திருத்திக்கொண்டிருந்தபோது வேறொருவர் இந்தப் பங்கை மாற்றினார். அவர்களின் மாற்றத்தைக் காண பக்கத்தை மீண்டும் ஏற்றி, உங்கள் மாற்றத்தை மீண்டும் செய்யுங்கள். | | |
| `Access.errors.invalid_version` | Reload the page, then make your change again. | පිටුව නැවත පූරණය කර, ඔබේ වෙනස නැවත කරන්න. | பக்கத்தை மீண்டும் ஏற்றி, உங்கள் மாற்றத்தை மீண்டும் செய்யுங்கள். | | |
| `Access.errors.last_owner` | This is the institution's only owner. Make someone else an owner first. | මෙය ආයතනයේ එකම හිමිකරු ය. මුලින්ම වෙනත් අයෙකු හිමිකරුවෙකු කරන්න. | இது நிறுவனத்தின் ஒரே உரிமையாளர். முதலில் வேறொருவரை உரிமையாளராக்குங்கள். | | |

## The living sidebar (2026-10-31, `Shell`, plan 05 X2)

The new sidebar's words: folding it, the count beside a link, jump-to (`Ctrl K`), and today's
table. Drafts, written by the developer; please check them.

Questions for the reviewers:

1. `අද දවසේ ආහාර වේල්` / `இன்றைய உணவு` for "Today's table" (the day's meals and how full each
   is). Kept faith-neutral on purpose (`දානය` would read as Buddhist); does it read naturally?
2. `වෙත යන්න` / `இதற்குச் செல்` as the name of a search box that takes you to a screen.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Shell.sidebar` | Sidebar | පැති තීරුව | பக்கப்பட்டி | | |
| `Shell.fold` | Fold the sidebar | පැති තීරුව හකුළන්න | பக்கப்பட்டியைச் சுருக்கு | | |
| `Shell.unfold` | Open the sidebar | පැති තීරුව විවෘත කරන්න | பக்கப்பட்டியைத் திற | | |
| `Shell.badge` | # waiting | #ක් රැඳී සිටී | # காத்திருக்கிறது / காத்திருக்கின்றன | | |
| `Shell.jump.title` | Jump to | වෙත යන්න | இதற்குச் செல் | | |
| `Shell.jump.placeholder` | Type a screen's name | තිරයක නම ටයිප් කරන්න | ஒரு திரையின் பெயரைத் தட்டச்சு செய்யுங்கள் | | |
| `Shell.jump.results` | Screens | තිර | திரைகள் | | |
| `Shell.jump.count` | # screen(s) | තිර # | # திரை / திரைகள் | | |
| `Shell.jump.nothing` | No screen matches “{query}”. | “{query}” ට ගැළපෙන තිරයක් නැත. | “{query}” உடன் பொருந்தும் திரை இல்லை. | | |
| `Shell.jump.keys` | ↑ ↓ to move · Enter to open · Esc to close | ↑ ↓ ගමන් කිරීමට · Enter විවෘත කිරීමට · Esc වැසීමට | ↑ ↓ நகர · Enter திறக்க · Esc மூட | | |
| `Shell.today.title` | Today's table | අද දවසේ ආහාර වේල් | இன்றைய உணவு | | |
| `Shell.today.none` | No meals today. | අද ආහාර වේල් නැත. | இன்று உணவு வேளைகள் இல்லை. | | |
| `Shell.today.places` | {booked} of {max} taken | {max} න් {booked}ක් වෙන් වී ඇත | {max} இல் {booked} பதிவாகியுள்ளன | | |
| `Shell.today.full` | Every place taken | සියලු ස්ථාන වෙන් වී ඇත | எல்லா இடங்களும் பதிவாகிவிட்டன | | |
| `Shell.today.closed` | Closed today | අද වසා ඇත | இன்று மூடப்பட்டுள்ளது | | |
| `Shell.today.requested` | # to answer | පිළිතුරු දීමට # | # பதில் தேவை | | |

## The Day Ledger dashboard (2026-10-31, `Dashboard`, `Shell.jump`, plan 05 X3)

The new dashboard's words: the one sentence that says what today needs, the panels' titles, the
ledger's lines, and jump-to opening a booking by its number. Drafts, written by the developer;
please check them. `{…}` are filled in by the app: a meal's name, a place's name, a time, a date in
words, or a number.

Questions for the reviewers:

1. `මාසය, බඳුනෙන් බඳුනට` / `மாதம், கிண்ணம் கிண்ணமாக` for "Month, bowl by bowl" (each day of the
   month drawn as a bowl as full as its places). Does the picture carry over, or is a plain
   "This month" better?
2. `ලේඛනය` / `பதிவேடு` for "The ledger" (the latest changes of the bookings, newest first).
3. The ledger's lines put the donor's name, the meal and the date into one sentence
   (`{donor} ගේ {date} {meal} තහවුරු කළා.`). Do they read naturally with real names and dates?
4. `ඔබ ආහාර දෙන ස්ථාන` / `நீங்கள் உணவு வழங்கும் இடங்கள்` for "Places you give to": kept
   faith-neutral on purpose (`දන් දෙන` would read as Buddhist).

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Dashboard.sentence.waitingAndNext` | {count, plural, one {# booking waits} other {# bookings wait}} for your answer, and next on the table is {meal} at {time}. | {count, plural, one {වෙන්කිරීම් #ක්} other {වෙන්කිරීම් #ක්}} ඔබේ පිළිතුර බලාපොරොත්තුවෙන්; මීළඟට {time} ට {meal}. | {count, plural, one {# முன்பதிவு உங்கள் பதிலுக்காகக் காத்திருக்கிறது} other {# முன்பதிவுகள் உங்கள் பதிலுக்காகக் காத்திருக்கின்றன}}; அடுத்தது {time} மணிக்கு {meal}. | | |
| `Dashboard.sentence.waiting` | {count, plural, one {# booking waits} other {# bookings wait}} for your answer. | {count, plural, one {වෙන්කිරීම් #ක්} other {වෙන්කිරීම් #ක්}} ඔබේ පිළිතුර බලාපොරොත්තුවෙන්. | {count, plural, one {# முன்பதிவு உங்கள் பதிலுக்காகக் காத்திருக்கிறது.} other {# முன்பதிவுகள் உங்கள் பதிலுக்காகக் காத்திருக்கின்றன.}} | | |
| `Dashboard.sentence.institutionPlain` | Everything you look after is in the menu. | ඔබ බලාගන්නා සියල්ල මෙනුවේ ඇත. | நீங்கள் கவனிக்கும் அனைத்தும் பட்டியலில் உள்ளன. | | |
| `Dashboard.sentence.noMeals` | No meals are on the table today. | අද ආහාර වේල් නැත. | இன்று உணவு வேளைகள் இல்லை. | | |
| `Dashboard.sentence.mealsOver` | Today's meals have all begun; the calendar holds what comes next. | අද දවසේ ආහාර වේල් සියල්ල ආරම්භ වී ඇත; ඊළඟට එන දේ දින දර්ශනයේ ඇත. | இன்றைய உணவு வேளைகள் அனைத்தும் தொடங்கிவிட்டன; அடுத்து வருவது நாட்காட்டியில் உள்ளது. | | |
| `Dashboard.sentence.nextWithOpen` | Next on the table is {meal} at {time}, and {open, plural, one {# place is} other {# places are}} still open today. | මීළඟට {time} ට {meal}; අද තවම {open, plural, one {ස්ථාන #ක්} other {ස්ථාන #ක්}} විවෘතයි. | அடுத்தது {time} மணிக்கு {meal}; இன்று இன்னும் {open, plural, one {# இடம் திறந்துள்ளது} other {# இடங்கள் திறந்துள்ளன}}. | | |
| `Dashboard.sentence.nextAllGiven` | Next on the table is {meal} at {time}, and every meal still to come has its sponsor. | මීළඟට {time} ට {meal}; ඉදිරියට ඇති සෑම ආහාර වේලකටම දායකයෙක් සිටී. | அடுத்தது {time} மணிக்கு {meal}; வரவிருக்கும் ஒவ்வொரு உணவு வேளைக்கும் ஒரு நன்கொடையாளர் உள்ளார். | | |
| `Dashboard.sentence.donorPlain` | Find a place, and give a meal on a day that matters to you. | ස්ථානයක් සොයා, ඔබට වැදගත් දිනයක ආහාර වේලක් දෙන්න. | ஓர் இடத்தைக் கண்டறிந்து, உங்களுக்கு முக்கியமான ஒரு நாளில் உணவு வழங்குங்கள். | | |
| `Dashboard.sentence.nothingBooked` | You have no meal booked yet. Find a place, and choose a day. | ඔබ තවම ආහාර වේලක් වෙන් කර නැත. ස්ථානයක් සොයා දිනයක් තෝරන්න. | நீங்கள் இன்னும் எந்த உணவு வேளையையும் முன்பதிவு செய்யவில்லை. ஓர் இடத்தைக் கண்டறிந்து ஒரு நாளைத் தேர்ந்தெடுங்கள். | | |
| `Dashboard.sentence.givingToday` | Today you give {meal} at {place}, at {time}. | අද {time} ට ඔබ {place} හි {meal} දෙනවා. | இன்று {time} மணிக்கு {place} இல் நீங்கள் {meal} வழங்குகிறீர்கள். | | |
| `Dashboard.sentence.givingTomorrow` | Tomorrow you give {meal} at {place}, at {time}. | හෙට {time} ට ඔබ {place} හි {meal} දෙනවා. | நாளை {time} மணிக்கு {place} இல் நீங்கள் {meal} வழங்குகிறீர்கள். | | |
| `Dashboard.sentence.givingLater` | Your next meal to give is {meal} at {place}, on {date} at {time}. | ඔබේ මීළඟ ආහාර වේල: {date} {time} ට {place} හි {meal}. | நீங்கள் வழங்கும் அடுத்த உணவு: {date} {time} மணிக்கு {place} இல் {meal}. | | |
| `Dashboard.sentence.askedToday` | Your meal today, {meal} at {place} at {time}, is still waiting for their answer. | අද {time} ට {place} හි ඔබේ {meal} තවම ඔවුන්ගේ පිළිතුර බලාපොරොත්තුවෙන්. | இன்று {time} மணிக்கு {place} இல் உங்கள் {meal} இன்னும் அவர்களின் பதிலுக்காகக் காத்திருக்கிறது. | | |
| `Dashboard.sentence.askedTomorrow` | Your meal tomorrow, {meal} at {place} at {time}, is still waiting for their answer. | හෙට {time} ට {place} හි ඔබේ {meal} තවම ඔවුන්ගේ පිළිතුර බලාපොරොත්තුවෙන්. | நாளை {time} மணிக்கு {place} இல் உங்கள் {meal} இன்னும் அவர்களின் பதிலுக்காகக் காத்திருக்கிறது. | | |
| `Dashboard.sentence.askedLater` | Your next meal, {meal} at {place} on {date}, is still waiting for their answer. | {date} {place} හි ඔබේ {meal} තවම ඔවුන්ගේ පිළිතුර බලාපොරොත්තුවෙන්. | {date} அன்று {place} இல் உங்கள் {meal} இன்னும் அவர்களின் பதிலுக்காகக் காத்திருக்கிறது. | | |
| `Dashboard.sentence.platformPlain` | Every institution on Sethika, in one place. | Sethika හි සෑම ආයතනයක්ම, එක තැනක. | Sethika இல் உள்ள ஒவ்வொரு நிறுவனமும், ஒரே இடத்தில். | | |
| `Dashboard.sentence.queueClear` | No registration is waiting for a review. | සමාලෝචනය සඳහා කිසිදු ලියාපදිංචියක් රැඳී නැත. | மதிப்பாய்வுக்காக எந்தப் பதிவும் காத்திருக்கவில்லை. | | |
| `Dashboard.sentence.queue` | {count, plural, one {# registration waits} other {# registrations wait}} for a review; the oldest has waited {days, plural, =0 {since today} one {# day} other {# days}}. | {count, plural, one {ලියාපදිංචි #ක්} other {ලියාපදිංචි #ක්}} සමාලෝචනය බලාපොරොත්තුවෙන්; පැරණිතම එක {days, plural, =0 {අද සිට} one {දින #ක් සිට} other {දින #ක් සිට}} රැඳී සිටී. | {count, plural, one {# பதிவு மதிப்பாய்வுக்காகக் காத்திருக்கிறது} other {# பதிவுகள் மதிப்பாய்வுக்காகக் காத்திருக்கின்றன}}; பழையது {days, plural, =0 {இன்று முதல்} one {# நாளாக} other {# நாட்களாக}} காத்திருக்கிறது. | | |
| `Dashboard.panels.today` | Today's table | අද දවසේ ආහාර වේල් | இன்றைய உணவு | | |
| `Dashboard.panels.openDay` | Open today | අද දිනය විවෘත කරන්න | இன்றைத் திற | | |
| `Dashboard.panels.waiting` | Waiting for you | ඔබ බලාපොරොත්තුවෙන් | உங்களுக்காகக் காத்திருப்பவை | | |
| `Dashboard.panels.allWaiting` | All {count} on the calendar | දින දර්ශනයේ සියල්ල ({count}) | நாட்காட்டியில் அனைத்தும் ({count}) | | |
| `Dashboard.panels.nothingWaiting` | Nothing is waiting for an answer. | පිළිතුරක් බලාපොරොත්තුවෙන් කිසිවක් නැත. | பதிலுக்காக எதுவும் காத்திருக்கவில்லை. | | |
| `Dashboard.panels.ledger` | The ledger | ලේඛනය | பதிவேடு | | |
| `Dashboard.panels.month` | Month, bowl by bowl | මාසය, බඳුනෙන් බඳුනට | மாதம், கிண்ணம் கிண்ணமாக | | |
| `Dashboard.panels.openCalendar` | Open the calendar | දින දර්ශනය විවෘත කරන්න | நாட்காட்டியைத் திற | | |
| `Dashboard.panels.stale` | Some of today's figures could not be read. | අද දවසේ සමහර තොරතුරු කියවිය නොහැකි විය. | இன்றைய சில விவரங்களைப் படிக்க முடியவில்லை. | | |
| `Dashboard.panels.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Dashboard.panels.yourTable` | Your table | ඔබේ ආහාර වේල් | உங்கள் உணவு வேளைகள் | | |
| `Dashboard.panels.allBookings` | All my bookings | මගේ සියලු වෙන්කිරීම් | எனது அனைத்து முன்பதிவுகளும் | | |
| `Dashboard.panels.nothingComing` | No meal to give yet. | තවම දීමට ආහාර වේලක් නැත. | வழங்க இன்னும் உணவு வேளை இல்லை. | | |
| `Dashboard.panels.places` | Places you give to | ඔබ ආහාර දෙන ස්ථාන | நீங்கள் உணவு வழங்கும் இடங்கள் | | |
| `Dashboard.panels.queue` | Waiting for a review | සමාලෝචනය බලාපොරොත්තුවෙන් | மதிப்பாய்வுக்காகக் காத்திருப்பவை | | |
| `Dashboard.panels.allQueue` | All {count} in the queue | පෝලිමේ සියල්ල ({count}) | வரிசையில் அனைத்தும் ({count}) | | |
| `Dashboard.panels.openQueue` | Open the queue | පෝලිම විවෘත කරන්න | வரிசையைத் திற | | |
| `Dashboard.panels.queueClear` | The queue is clear. | පෝලිම හිස්ය. | வரிசை காலியாக உள்ளது. | | |
| `Dashboard.panels.waited` | {days, plural, =0 {today} one {# day} other {# days}} | {days, plural, =0 {අද} one {දින #} other {දින #}} | {days, plural, =0 {இன்று} one {# நாள்} other {# நாட்கள்}} | | |
| `Dashboard.panels.decisions` | Latest decisions | නවතම තීරණ | சமீபத்திய முடிவுகள் | | |
| `Dashboard.panels.noDecisions` | No application has been decided yet. | තවම කිසිදු අයදුම්පතක් තීරණය කර නැත. | இன்னும் எந்த விண்ணப்பமும் முடிவு செய்யப்படவில்லை. | | |
| `Dashboard.panels.institutions` | Institutions by status | තත්ත්වය අනුව ආයතන | நிலை வாரியாக நிறுவனங்கள் | | |
| `Dashboard.panels.openInstitutions` | All institutions | සියලු ආයතන | அனைத்து நிறுவனங்களும் | | |
| `Dashboard.timeline.now` | Now | දැන් | இப்போது | | |
| `Dashboard.month.day` | {taken} of {places} places taken. | ස්ථාන {places} න් {taken}ක් වෙන් වී ඇත. | {places} இடங்களில் {taken} பதிவாகியுள்ளன. | | |
| `Dashboard.month.waiting` | {count, plural, one {# request waits.} other {# requests wait.}} | {count, plural, one {ඉල්ලීම් #ක් රැඳී ඇත.} other {ඉල්ලීම් #ක් රැඳී ඇත.}} | {count, plural, one {# கோரிக்கை காத்திருக்கிறது.} other {# கோரிக்கைகள் காத்திருக்கின்றன.}} | | |
| `Dashboard.month.nothing` | Nothing booked. | කිසිවක් වෙන් කර නැත. | எதுவும் பதிவு செய்யப்படவில்லை. | | |
| `Dashboard.ledger.none` | No booking has changed yet. | තවම කිසිදු වෙන්කිරීමක් වෙනස් වී නැත. | இன்னும் எந்த முன்பதிவும் மாறவில்லை. | | |
| `Dashboard.ledger.line.Requested` | {donor} booked {meal} for {date}. | {donor} {date} සඳහා {meal} වෙන් කළා. | {donor} {date} க்கு {meal} முன்பதிவு செய்தார். | | |
| `Dashboard.ledger.line.Approved` | {donor}'s {meal} on {date} was confirmed. | {donor} ගේ {date} {meal} තහවුරු කළා. | {donor} இன் {date} {meal} உறுதி செய்யப்பட்டது. | | |
| `Dashboard.ledger.line.Rejected` | {donor}'s {meal} on {date} was not accepted. | {donor} ගේ {date} {meal} පිළිගත්තේ නැත. | {donor} இன் {date} {meal} ஏற்கப்படவில்லை. | | |
| `Dashboard.ledger.line.Cancelled` | {donor}'s {meal} on {date} was cancelled. | {donor} ගේ {date} {meal} අවලංගු කළා. | {donor} இன் {date} {meal} ரத்து செய்யப்பட்டது. | | |
| `Dashboard.ledger.line.Completed` | {donor}'s {meal} on {date} was served. | {donor} ගේ {date} {meal} සපයා අවසන්. | {donor} இன் {date} {meal} வழங்கப்பட்டது. | | |
| `Dashboard.ledger.line.NoShow` | {donor}'s {meal} on {date} did not happen. | {donor} ගේ {date} {meal} සිදු නොවීය. | {donor} இன் {date} {meal} நடைபெறவில்லை. | | |
| `Dashboard.status.Onboarding` | Onboarding | ඇතුළත් වෙමින් | இணைகிறது | | |
| `Dashboard.status.Active` | Active | ක්‍රියාත්මකයි | செயலில் | | |
| `Dashboard.status.Suspended` | Suspended | අත්හිටුවා ඇත | இடைநிறுத்தப்பட்டது | | |
| `Dashboard.status.Deactivated` | Deactivated | අක්‍රියයි | செயலிழக்கப்பட்டது | | |
| `Dashboard.decision.Approved` | {name} was approved. | {name} අනුමත කළා. | {name} அங்கீகரிக்கப்பட்டது. | | |
| `Dashboard.decision.Rejected` | {name} was not accepted. | {name} පිළිගත්තේ නැත. | {name} ஏற்கப்படவில்லை. | | |
| `Shell.jump.booking` | Open booking {bookingNo} | {bookingNo} වෙන්කිරීම විවෘත කරන්න | {bookingNo} முன்பதிவைத் திற | | |
| `Shell.jump.bookingHint` | On its day in the calendar | දින දර්ශනයේ එහි දිනයේ | நாட்காட்டியில் அதன் நாளில் | | |
| `Shell.jump.bookingMissing` | This institution has no booking {bookingNo}. | මෙම ආයතනයට {bookingNo} වෙන්කිරීමක් නැත. | இந்த நிறுவனத்திடம் {bookingNo} முன்பதிவு இல்லை. | | |
| `Shell.jump.bookingFailed` | The booking could not be looked up. Please try again. | වෙන්කිරීම සෙවිය නොහැකි විය. නැවත උත්සාහ කරන්න. | முன்பதிவைத் தேட முடியவில்லை. மீண்டும் முயலுங்கள். | | |

## The platform lists' filters and pages (2026-11-01, `Admin`, `Errors`, plan 05 X4)

The users and institutions lists now show a page at a time, with filters, an order and the page
in the address; and a step-up that was closed says nothing changed. Drafts, written by the
developer; please check them. `{from}–{to} of {total}` reads like "25–48 of 312".

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Admin.list.all` | All | සියල්ල | அனைத்தும் | | |
| `Admin.list.showing` | {from}–{to} of {total} | {total} න් {from}–{to} | {total} இல் {from}–{to} | | |
| `Admin.list.noneOf` | None of {total} | {total} න් කිසිවක් නැත | {total} இல் எதுவும் இல்லை | | |
| `Admin.list.pages` | Pages | පිටු | பக்கங்கள் | | |
| `Admin.list.previous` | Previous | පෙර | முந்தையது | | |
| `Admin.list.next` | Next | ඊළඟ | அடுத்தது | | |
| `Admin.list.page` | Page {page} of {count} | {count} න් {page} වන පිටුව | {count} இல் பக்கம் {page} | | |
| `Admin.users.kind` | Kind of account | ගිණුමේ වර්ගය | கணக்கின் வகை | | |
| `Admin.users.statusLabel` | Status | තත්ත්වය | நிலை | | |
| `Admin.users.sort` | Order | අනුපිළිවෙළ | வரிசை | | |
| `Admin.users.sortNewest` | Newest first | නවතම මුලින් | புதியவை முதலில் | | |
| `Admin.users.sortName` | By name | නම අනුව | பெயர் வாரியாக | | |
| `Admin.institutions.statusLabel` | Status | තත්ත්වය | நிலை | | |
| `Admin.institutions.type` | Type | වර්ගය | வகை | | |
| `Admin.institutions.allTypes` | All types | සියලු වර්ග | அனைத்து வகைகளும் | | |
| `Admin.institutions.district` | District | දිස්ත්‍රික්කය | மாவட்டம் | | |
| `Admin.institutions.allDistricts` | All districts | සියලු දිස්ත්‍රික්ක | அனைத்து மாவட்டங்களும் | | |
| `Admin.institutions.sort` | Order | අනුපිළිවෙළ | வரிசை | | |
| `Admin.institutions.sortName` | By name | නම අනුව | பெயர் வாரியாக | | |
| `Admin.institutions.sortNewest` | Newest first | නවතම මුලින් | புதியவை முதலில் | | |
| `Admin.institutions.list` | Institutions | ආයතන | நிறுவனங்கள் | | |
| `Errors.stepup_required` | Nothing was changed: the extra check was not finished. | කිසිවක් වෙනස් නොවීය: අමතර තහවුරු කිරීම අවසන් කළේ නැත. | எதுவும் மாற்றப்படவில்லை: கூடுதல் சரிபார்ப்பு முடிக்கப்படவில்லை. | | |
| `Admin.users.institution` | Institution | ආයතනය | நிறுவனம் | | |
| `Admin.users.institutionHint` | Its people: anyone holding a role there now | එහි අය: දැනට එහි භූමිකාවක් දරන ඕනෑම අයෙක් | அதன் நபர்கள்: தற்போது அங்கு ஒரு பங்கை வகிக்கும் எவரும் | | |
| `Admin.users.institutionClear` | Every institution | සියලු ආයතන | அனைத்து நிறுவனங்களும் | | |
| `Admin.users.institutionNone` | No institution matches. | කිසිදු ආයතනයක් ගැළපෙන්නේ නැත. | எந்த நிறுவனமும் பொருந்தவில்லை. | | |

## Religious service screens (2026-11-01, `Services`, `Shell`, Phase E 8.9)

The services a place offers on its page and the request sheet, the donor's "My services", and the
institution's service types, leaders (usual week and leave), appointments and a leader's own
schedule. Drafts, written by the developer; please check them. `{...}` are filled in by the app
(a name, a date, a number); `#` in a plural is the number.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Services.status.Requested` | Waiting for a reply | පිළිතුර බලාපොරොත්තුවෙන් | பதிலுக்காகக் காத்திருக்கிறது | | |
| `Services.status.Approved` | Confirmed | තහවුරුයි | உறுதியானது | | |
| `Services.status.Rejected` | Not accepted | පිළිගත්තේ නැත | ஏற்கப்படவில்லை | | |
| `Services.status.Cancelled` | Cancelled | අවලංගුයි | ரத்து செய்யப்பட்டது | | |
| `Services.status.Completed` | Held | පැවැත්වුණා | நடைபெற்றது | | |
| `Services.duration.hours` | {count, plural, one {# hour} other {# hours}} | {count, plural, one {පැය #} other {පැය #}} | {count, plural, one {# மணி நேரம்} other {# மணி நேரம்}} | | |
| `Services.duration.minutes` | {count} min | විනාඩි {count} | {count} நிமிடம் | | |
| `Services.duration.hoursMinutes` | {hours, plural, one {# hour} other {# hours}} {minutes} min | {hours, plural, one {පැය #} other {පැය #}} විනාඩි {minutes} | {hours, plural, one {# மணி} other {# மணி}} {minutes} நிமிடம் | | |
| `Services.errors.service_not_found` | This service is no longer offered. | මෙම සේවාව තවදුරටත් ලබා නොදේ. | இந்தச் சேவை இனி வழங்கப்படுவதில்லை. | | |
| `Services.errors.service_inactive` | This service is not taking requests just now. | මෙම සේවාව දැනට ඉල්ලීම් භාර නොගනී. | இந்தச் சேவை தற்போது கோரிக்கைகளை ஏற்கவில்லை. | | |
| `Services.errors.institution_not_active` | This place is not taking requests just now. | මෙම ස්ථානය දැනට ඉල්ලීම් භාර නොගනී. | இந்த இடம் தற்போது கோரிக்கைகளை ஏற்கவில்லை. | | |
| `Services.errors.not_on_the_grid` | Please choose a start time on the quarter hour. | කරුණාකර විනාඩි 15 පරතරයකින් ආරම්භක වේලාවක් තෝරන්න. | கால் மணி இடைவெளியில் ஒரு தொடக்க நேரத்தைத் தேர்ந்தெடுங்கள். | | |
| `Services.errors.location_not_offered` | This service is not held there. | මෙම සේවාව එහි පවත්වනු නොලැබේ. | இந்தச் சேவை அங்கே நடத்தப்படுவதில்லை. | | |
| `Services.errors.address_required` | Please give the address where it will be held. | කරුණාකර එය පවත්වන ලිපිනය දෙන්න. | அது நடைபெறும் முகவரியைத் தாருங்கள். | | |
| `Services.errors.too_soon` | That day is too soon to ask for. Please choose a later day. | එම දිනය ඉල්ලීමට ඉතා ළඟයි. කරුණාකර පසු දිනයක් තෝරන්න. | அந்த நாள் கோருவதற்கு மிக அருகில் உள்ளது. பிந்தைய நாளைத் தேர்ந்தெடுங்கள். | | |
| `Services.errors.too_far` | That day is too far ahead to ask for yet. | එම දිනය ඉල්ලීමට තවම ඉතා දුරයි. | அந்த நாள் இன்னும் கோர முடியாத அளவுக்குத் தொலைவில் உள்ளது. | | |
| `Services.errors.phone_required` | Your account needs a confirmed phone number before you can ask. | ඉල්ලීමට පෙර ඔබගේ ගිණුමට තහවුරු කළ දුරකථන අංකයක් අවශ්‍යයි. | கோருவதற்கு உங்கள் கணக்கில் உறுதிப்படுத்தப்பட்ட தொலைபேசி எண் தேவை. | | |
| `Services.errors.invalid_details` | Some details need fixing. | සමහර විස්තර නිවැරදි කළ යුතුයි. | சில விவரங்களைத் திருத்த வேண்டும். | | |
| `Services.errors.appointment_not_found` | We could not find this appointment. | මෙම සේවා වාරය සොයාගත නොහැකි විය. | இந்த நியமனத்தைக் கண்டறிய முடியவில்லை. | | |
| `Services.errors.invalid_transition` | This appointment has already changed. The list has been refreshed. | මෙම සේවා වාරය දැනටමත් වෙනස් වී ඇත. ලැයිස්තුව යාවත්කාලීන කළා. | இந்த நியமனம் ஏற்கனவே மாறிவிட்டது. பட்டியல் புதுப்பிக்கப்பட்டது. | | |
| `Services.errors.cancel_window_closed` | It is too late to cancel this yourself. Please call the institution. | මෙය ඔබටම අවලංගු කිරීමට ප්‍රමාද වැඩියි. කරුණාකර ආයතනයට කතා කරන්න. | இதை நீங்களே ரத்து செய்யும் நேரம் கடந்துவிட்டது. நிறுவனத்தை அழையுங்கள். | | |
| `Services.errors.too_early` | It can be marked as held only once it has ended. | මෙය පැවැත්වූ බව සලකුණු කළ හැක්කේ එය අවසන් වූ පසු පමණි. | இது முடிந்த பின்னரே நடைபெற்றதாகக் குறிக்க முடியும். | | |
| `Services.errors.already_over` | This appointment has already ended. | මෙම සේවා වාරය දැනටමත් අවසන්. | இந்த நியமனம் ஏற்கனவே முடிந்துவிட்டது. | | |
| `Services.errors.reason_required` | Please give a reason. | කරුණාකර හේතුවක් දෙන්න. | காரணத்தைத் தாருங்கள். | | |
| `Services.errors.reason_too_long` | Please keep the reason under 300 characters. | කරුණාකර හේතුව අක්ෂර 300කට අඩුවෙන් තබන්න. | காரணத்தை 300 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.errors.stale` | Someone else changed this just now. It has been refreshed; please check and try again. | වෙනත් කෙනෙක් මේ දැන් මෙය වෙනස් කළා. එය යාවත්කාලීන කළා; කරුණාකර පරීක්ෂා කර නැවත උත්සාහ කරන්න. | வேறொருவர் இப்போதுதான் இதை மாற்றினார். அது புதுப்பிக்கப்பட்டது; சரிபார்த்து மீண்டும் முயலுங்கள். | | |
| `Services.errors.leader_not_free` | A leader you chose is not free at that time. | ඔබ තෝරාගත් ආගමික නායකයෙකු එම වේලාවේ නිදහස් නැත. | நீங்கள் தேர்ந்தெடுத்த ஒரு சமயத் தலைவர் அந்த நேரத்தில் இல்லை. | | |
| `Services.errors.try_again` | Someone is assigning that leader right now. Please try again in a moment. | එම නායකයා මේ මොහොතේ වෙනත් අයෙකු පවරමින් සිටී. කරුණාකර මොහොතකින් නැවත උත්සාහ කරන්න. | அந்தத் தலைவரை இப்போது வேறொருவர் நியமித்துக்கொண்டிருக்கிறார். சிறிது நேரத்தில் மீண்டும் முயலுங்கள். | | |
| `Services.errors.already_assigned` | That leader is already assigned. | එම නායකයා දැනටමත් පවරා ඇත. | அந்தத் தலைவர் ஏற்கனவே நியமிக்கப்பட்டுள்ளார். | | |
| `Services.errors.too_many_leaders` | This service needs no more leaders. | මෙම සේවාවට තවත් නායකයින් අවශ්‍ය නැත. | இந்தச் சேவைக்கு மேலும் தலைவர்கள் தேவையில்லை. | | |
| `Services.errors.not_assigned` | That leader is not assigned to this appointment. | එම නායකයා මෙම සේවා වාරයට පවරා නැත. | அந்தத் தலைவர் இந்த நியமனத்துக்கு நியமிக்கப்படவில்லை. | | |
| `Services.errors.leader_unavailable` | That leader is not on your roster, or is inactive. | එම නායකයා ඔබගේ ලැයිස්තුවේ නැත, නැතහොත් අක්‍රියයි. | அந்தத் தலைவர் உங்கள் பட்டியலில் இல்லை, அல்லது செயலில் இல்லை. | | |
| `Services.errors.not_a_leader` | Your account is not on this institution's roster of leaders. | ඔබගේ ගිණුම මෙම ආයතනයේ ආගමික නායක ලැයිස්තුවේ නැත. | உங்கள் கணக்கு இந்த நிறுவனத்தின் சமயத் தலைவர் பட்டியலில் இல்லை. | | |
| `Services.errors.service_name_taken` | You already offer a service with that name. | ඔබ දැනටමත් එම නමින් සේවාවක් ලබා දෙයි. | அந்தப் பெயரில் ஏற்கனவே ஒரு சேவையை வழங்குகிறீர்கள். | | |
| `Services.errors.service_type_not_found` | We could not find this service. | මෙම සේවාව සොයාගත නොහැකි විය. | இந்தச் சேவையைக் கண்டறிய முடியவில்லை. | | |
| `Services.errors.leader_not_found` | We could not find this leader. | මෙම නායකයා සොයාගත නොහැකි විය. | இந்தத் தலைவரைக் கண்டறிய முடியவில்லை. | | |
| `Services.errors.time_off_not_found` | This leave is no longer recorded. | මෙම නිවාඩුව තවදුරටත් වාර්තා වී නැත. | இந்த விடுப்பு இனி பதிவில் இல்லை. | | |
| `Services.errors.account_not_found` | No Sethika account has that phone number. The leader can sign up first, or be added without one. | එම දුරකථන අංකයට Sethika ගිණුමක් නැත. නායකයාට පළමුව ලියාපදිංචි විය හැක, නැතහොත් ගිණුමක් නොමැතිව එක් කළ හැක. | அந்தத் தொலைபேசி எண்ணுக்கு Sethika கணக்கு இல்லை. தலைவர் முதலில் பதிவுசெய்யலாம், அல்லது கணக்கின்றிச் சேர்க்கலாம். | | |
| `Services.errors.account_already_linked` | That account is already linked to another leader on your roster. | එම ගිණුම ඔබගේ ලැයිස්තුවේ වෙනත් නායකයෙකුට දැනටමත් සම්බන්ධයි. | அந்தக் கணக்கு உங்கள் பட்டியலில் உள்ள வேறொரு தலைவருடன் ஏற்கனவே இணைக்கப்பட்டுள்ளது. | | |
| `Services.errors.time_off_clashes` | The leader is assigned to a service in that time. Reassign it first, then add the leave. | එම කාලයේ නායකයා සේවාවකට පවරා ඇත. පළමුව එය වෙනත් අයෙකුට පවරා, ඉන්පසු නිවාඩුව එක් කරන්න. | அந்த நேரத்தில் தலைவர் ஒரு சேவைக்கு நியமிக்கப்பட்டுள்ளார். முதலில் அதை வேறொருவருக்கு நியமித்து, பின்னர் விடுப்பைச் சேருங்கள். | | |
| `Services.errors.time_off_overlaps` | The leader already has leave in that time. | එම කාලයේ නායකයාට දැනටමත් නිවාඩු ඇත. | அந்த நேரத்தில் தலைவருக்கு ஏற்கனவே விடுப்பு உள்ளது. | | |
| `Services.errors.invalid_view` | That list is not available. | එම ලැයිස්තුව ලබා ගත නොහැක. | அந்தப் பட்டியல் கிடைக்கவில்லை. | | |
| `Services.offered.title` | Religious services | ආගමික සේවා | சமயச் சேவைகள் | | |
| `Services.offered.intro` | Ask them to hold a service for you. They confirm each request and send their religious leaders. | ඔබ වෙනුවෙන් සේවාවක් පැවැත්වීමට ඉල්ලන්න. ඔවුන් සෑම ඉල්ලීමක්ම තහවුරු කර ආගමික නායකයින් එවයි. | உங்களுக்காக ஒரு சேவையை நடத்தக் கோருங்கள். ஒவ்வொரு கோரிக்கையையும் அவர்கள் உறுதிப்படுத்தி, சமயத் தலைவர்களை அனுப்புவார்கள். | | |
| `Services.offered.about` | About {duration} | ආසන්න වශයෙන් {duration} | சுமார் {duration} | | |
| `Services.offered.where.AtInstitution` | Held at the institution | ආයතනයේදී පවත්වයි | நிறுவனத்தில் நடைபெறும் | | |
| `Services.offered.where.AtDonorPlace` | Held at your home | ඔබගේ නිවසේදී පවත්වයි | உங்கள் வீட்டில் நடைபெறும் | | |
| `Services.offered.where.Either` | At the institution or at your home | ආයතනයේදී හෝ ඔබගේ නිවසේදී | நிறுவனத்தில் அல்லது உங்கள் வீட்டில் | | |
| `Services.offered.advance` | {min, plural, =0 {Can be asked for the same day} one {Ask at least a day ahead} other {Ask at least # days ahead}} | {min, plural, =0 {එදිනම ඉල්ලිය හැක} one {අවම වශයෙන් දිනකට පෙර ඉල්ලන්න} other {අවම වශයෙන් දින #කට පෙර ඉල්ලන්න}} | {min, plural, =0 {அதே நாளிலும் கோரலாம்} one {குறைந்தது ஒரு நாள் முன்பே கோருங்கள்} other {குறைந்தது # நாட்கள் முன்பே கோருங்கள்}} | | |
| `Services.offered.ask` | Ask for this service | මෙම සේවාව ඉල්ලන්න | இந்தச் சேவையைக் கோருங்கள் | | |
| `Services.sheet.signInFirst` | Sign in to ask for this service. It takes a minute, and we will bring you straight back here. | මෙම සේවාව ඉල්ලීමට පුරනය වන්න. එයට විනාඩියක් ගත වේ, අපි ඔබව නැවත කෙළින්ම මෙහි ගෙන එන්නෙමු. | இந்தச் சேவையைக் கோர உள்நுழையுங்கள். ஒரு நிமிடம் ஆகும்; உங்களை நேராக இங்கே திரும்பக் கொண்டு வருவோம். | | |
| `Services.sheet.institutionSession` | You are signed in for an institution. To ask as yourself, sign out and sign in again without choosing an institution. | ඔබ ආයතනයක් වෙනුවෙන් පුරනය වී ඇත. ඔබ ලෙසම ඉල්ලීමට, ඉවත් වී ආයතනයක් නොතෝරා නැවත පුරනය වන්න. | நீங்கள் ஒரு நிறுவனத்துக்காக உள்நுழைந்துள்ளீர்கள். நீங்களாகவே கோர, வெளியேறி, நிறுவனத்தைத் தேர்ந்தெடுக்காமல் மீண்டும் உள்நுழையுங்கள். | | |
| `Services.sheet.notAllowed` | This account cannot ask for services. | මෙම ගිණුමට සේවා ඉල්ලිය නොහැක. | இந்தக் கணக்கால் சேவைகளைக் கோர முடியாது. | | |
| `Services.sheet.date` | Which day? | කුමන දිනයද? | எந்த நாள்? | | |
| `Services.sheet.dateHint` | The days they take requests for. | ඔවුන් ඉල්ලීම් භාර ගන්නා දින. | அவர்கள் கோரிக்கைகளை ஏற்கும் நாட்கள். | | |
| `Services.sheet.dateRequired` | Please choose a day. | කරුණාකර දිනයක් තෝරන්න. | ஒரு நாளைத் தேர்ந்தெடுங்கள். | | |
| `Services.sheet.notOffered` | Not open for requests | ඉල්ලීම් සඳහා විවෘත නැත | கோரிக்கைகளுக்குத் திறக்கப்படவில்லை | | |
| `Services.sheet.time` | Starting at | ආරම්භක වේලාව | தொடங்கும் நேரம் | | |
| `Services.sheet.timeHint` | It would end at about {end}. | එය ආසන්න වශයෙන් {end}ට අවසන් වේ. | இது சுமார் {end} மணிக்கு முடியும். | | |
| `Services.sheet.where` | Where? | කොහේද? | எங்கே? | | |
| `Services.sheet.atInstitution` | At {institution} | {institution} හිදී | {institution} இல் | | |
| `Services.sheet.atHome` | At my home | මගේ නිවසේදී | என் வீட்டில் | | |
| `Services.sheet.address` | Address | ලිපිනය | முகவரி | | |
| `Services.sheet.addressHint` | Where the leaders should come. Only the institution sees it. | නායකයින් පැමිණිය යුතු ස්ථානය. එය පෙනෙන්නේ ආයතනයට පමණි. | தலைவர்கள் வர வேண்டிய இடம். நிறுவனம் மட்டுமே இதைப் பார்க்கும். | | |
| `Services.sheet.addressRequired` | Please give the address. | කරුණාකර ලිපිනය දෙන්න. | முகவரியைத் தாருங்கள். | | |
| `Services.sheet.occasion` | Occasion (optional) | අවස්ථාව (අත්‍යවශ්‍ය නොවේ) | நிகழ்வு (விருப்பத்தேர்வு) | | |
| `Services.sheet.occasionHint` | A house-warming, an anniversary, a remembrance... | ගෘහ ප්‍රවේශයක්, සංවත්සරයක්, සිහිකිරීමක්... | புதுமனை புகுவிழா, ஆண்டு நிறைவு, நினைவு நாள்... | | |
| `Services.sheet.note` | A note for them (optional) | ඔවුන්ට සටහනක් (අත්‍යවශ්‍ය නොවේ) | அவர்களுக்கு ஒரு குறிப்பு (விருப்பத்தேர்வு) | | |
| `Services.sheet.noteHint` | Anything they should know. | ඔවුන් දැනගත යුතු ඕනෑම දෙයක්. | அவர்கள் அறிய வேண்டிய எதுவும். | | |
| `Services.sheet.approvalNote` | They confirm each request and assign their leaders. We will text you their reply. | ඔවුන් සෑම ඉල්ලීමක්ම තහවුරු කර නායකයින් පවරයි. ඔවුන්ගේ පිළිතුර අපි ඔබට කෙටි පණිවිඩයකින් දන්වන්නෙමු. | ஒவ்வொரு கோரிக்கையையும் அவர்கள் உறுதிப்படுத்தி, தலைவர்களை நியமிப்பார்கள். அவர்களின் பதிலைக் குறுஞ்செய்தி மூலம் அறிவிப்போம். | | |
| `Services.sheet.submit` | Send request | ඉල්ලීම යවන්න | கோரிக்கையை அனுப்புங்கள் | | |
| `Services.sheet.done` | Request sent | ඉල්ලීම යැවුණා | கோரிக்கை அனுப்பப்பட்டது | | |
| `Services.sheet.appointmentNo` | Your request number | ඔබගේ ඉල්ලීම් අංකය | உங்கள் கோரிக்கை எண் | | |
| `Services.sheet.doneNext` | We will text you when they reply. | ඔවුන් පිළිතුරු දුන් විට අපි ඔබට කෙටි පණිවිඩයක් එවන්නෙමු. | அவர்கள் பதிலளித்ததும் குறுஞ்செய்தி அனுப்புவோம். | | |
| `Services.sheet.seeMine` | See my service requests | මගේ සේවා ඉල්ලීම් බලන්න | என் சேவைக் கோரிக்கைகளைப் பாருங்கள் | | |
| `Services.sheet.close` | Close | වසන්න | மூடு | | |
| `Services.mine.title` | My service requests | මගේ සේවා ඉල්ලීම් | என் சேவைக் கோரிக்கைகள் | | |
| `Services.mine.intro` | The religious services you have asked for, and those still to come. | ඔබ ඉල්ලූ ආගමික සේවා සහ තවම ඉදිරියට ඇති ඒවා. | நீங்கள் கோரிய சமயச் சேவைகளும் இனி வரவிருப்பவையும். | | |
| `Services.mine.loadFailed` | Could not load your requests. | ඔබගේ ඉල්ලීම් පූරණය කළ නොහැකි විය. | உங்கள் கோரிக்கைகளை ஏற்ற முடியவில்லை. | | |
| `Services.mine.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Services.mine.none` | You have not asked for a religious service yet. | ඔබ තවම ආගමික සේවාවක් ඉල්ලා නැත. | நீங்கள் இன்னும் எந்தச் சமயச் சேவையையும் கோரவில்லை. | | |
| `Services.mine.find` | Find a place | ස්ථානයක් සොයන්න | ஓர் இடத்தைக் கண்டறியுங்கள் | | |
| `Services.mine.upcoming` | Coming up | ඉදිරියට | வரவிருப்பவை | | |
| `Services.mine.noneUpcoming` | Nothing coming up. | ඉදිරියට කිසිවක් නැත. | வரவிருப்பவை எதுவும் இல்லை. | | |
| `Services.mine.past` | Earlier | පෙර | முந்தையவை | | |
| `Services.mine.when` | {days, plural, =0 {Today} =1 {Tomorrow} other {In # days}} | {days, plural, =0 {අද} =1 {හෙට} other {දින #කින්}} | {days, plural, =0 {இன்று} =1 {நாளை} other {# நாட்களில்}} | | |
| `Services.mine.atHome` | At your home | ඔබගේ නිවසේදී | உங்கள் வீட்டில் | | |
| `Services.mine.atInstitution` | At the institution | ආයතනයේදී | நிறுவனத்தில் | | |
| `Services.mine.rejectedBecause` | Not accepted: {reason} | පිළිගත්තේ නැත: {reason} | ஏற்கப்படவில்லை: {reason} | | |
| `Services.mine.cancelledBecause` | Cancelled: {reason} | අවලංගු කළා: {reason} | ரத்து செய்யப்பட்டது: {reason} | | |
| `Services.mine.waiting` | Waiting for their reply. We will text you. | ඔවුන්ගේ පිළිතුර බලාපොරොත්තුවෙන්. අපි ඔබට කෙටි පණිවිඩයක් එවන්නෙමු. | அவர்களின் பதிலுக்காகக் காத்திருக்கிறது. குறுஞ்செய்தி அனுப்புவோம். | | |
| `Services.mine.cancel` | Cancel request | ඉල්ලීම අවලංගු කරන්න | கோரிக்கையை ரத்து செய்யுங்கள் | | |
| `Services.mine.cancelSheet.title` | Cancel this request? | මෙම ඉල්ලීම අවලංගු කරන්නද? | இந்தக் கோரிக்கையை ரத்து செய்யவா? | | |
| `Services.mine.cancelSheet.description` | {appointmentNo}. The leaders will be told they are not needed. | {appointmentNo}. නායකයින්ට ඔවුන් අවශ්‍ය නොවන බව දන්වනු ලැබේ. | {appointmentNo}. தலைவர்களுக்கு அவர்கள் தேவையில்லை என்று அறிவிக்கப்படும். | | |
| `Services.mine.cancelSheet.reason` | Reason (optional) | හේතුව (අත්‍යවශ්‍ය නොවේ) | காரணம் (விருப்பத்தேர்வு) | | |
| `Services.mine.cancelSheet.reasonHint` | The institution will see it. | ආයතනයට එය පෙනෙනු ඇත. | நிறுவனம் இதைப் பார்க்கும். | | |
| `Services.mine.cancelSheet.keep` | Keep request | ඉල්ලීම තබා ගන්න | கோரிக்கையை வைத்திருங்கள் | | |
| `Services.mine.cancelSheet.confirm` | Cancel request | ඉල්ලීම අවලංගු කරන්න | கோரிக்கையை ரத்து செய்யுங்கள் | | |
| `Services.mine.cancelSheet.done` | {appointmentNo} is cancelled. | {appointmentNo} අවලංගු කළා. | {appointmentNo} ரத்து செய்யப்பட்டது. | | |
| `Services.types.title` | Religious services | ආගමික සේවා | சமயச் சேவைகள் | | |
| `Services.types.intro` | The services you hold for donors: what each is called, how long it takes, how many leaders it needs and where it is held. Donors see the active ones on your page. | පරිත්‍යාගශීලීන් සඳහා ඔබ පවත්වන සේවා: එක් එක් සේවාවේ නම, ගත වන කාලය, අවශ්‍ය නායකයින් ගණන සහ පවත්වන ස්ථානය. ක්‍රියාකාරී ඒවා ඔබගේ පිටුවේ පෙනේ. | நன்கொடையாளர்களுக்காக நீங்கள் நடத்தும் சேவைகள்: ஒவ்வொன்றின் பெயர், எடுக்கும் நேரம், தேவையான தலைவர்கள், நடைபெறும் இடம். செயலில் உள்ளவை உங்கள் பக்கத்தில் தெரியும். | | |
| `Services.types.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Services.types.heading` | Services offered | ලබා දෙන සේවා | வழங்கப்படும் சேவைகள் | | |
| `Services.types.add` | Add a service | සේවාවක් එක් කරන්න | ஒரு சேவையைச் சேருங்கள் | | |
| `Services.types.none` | You offer no services yet. Add the first one, in your own words. | ඔබ තවම සේවා ලබා නොදේ. පළමු සේවාව ඔබගේම වචනවලින් එක් කරන්න. | நீங்கள் இன்னும் சேவைகளை வழங்கவில்லை. முதல் சேவையை உங்கள் சொந்த வார்த்தைகளில் சேருங்கள். | | |
| `Services.types.inactive` | Not offered | ලබා නොදේ | வழங்கப்படவில்லை | | |
| `Services.types.leaders` | {count, plural, one {# leader} other {# leaders}} | {count, plural, one {නායකයින් #} other {නායකයින් #}} | {count, plural, one {# தலைவர்} other {# தலைவர்கள்}} | | |
| `Services.types.where.AtInstitution` | Held here | මෙහිදී පවත්වයි | இங்கே நடைபெறும் | | |
| `Services.types.where.AtDonorPlace` | Held at the donor's home | පරිත්‍යාගශීලියාගේ නිවසේදී පවත්වයි | நன்கொடையாளரின் வீட்டில் நடைபெறும் | | |
| `Services.types.where.Either` | Here or at the donor's home | මෙහිදී හෝ පරිත්‍යාගශීලියාගේ නිවසේදී | இங்கே அல்லது நன்கொடையாளரின் வீட்டில் | | |
| `Services.types.travel` | {duration} travel each way | එක් පැත්තකට ගමනට {duration} | ஒரு வழிக்கு {duration} பயணம் | | |
| `Services.types.window` | Asked for {min}–{max} days ahead | දින {min}–{max}කට පෙර ඉල්ලිය හැක | {min}–{max} நாட்கள் முன்பே கோரலாம் | | |
| `Services.types.edit` | Edit | සංස්කරණය | திருத்துங்கள் | | |
| `Services.types.deactivate` | Stop offering | ලබා දීම නවත්වන්න | வழங்குவதை நிறுத்துங்கள் | | |
| `Services.types.activate` | Offer again | නැවත ලබා දෙන්න | மீண்டும் வழங்குங்கள் | | |
| `Services.types.form.newTitle` | Add a service | සේවාවක් එක් කරන්න | ஒரு சேவையைச் சேருங்கள் | | |
| `Services.types.form.editTitle` | Edit service | සේවාව සංස්කරණය | சேவையைத் திருத்துங்கள் | | |
| `Services.types.form.description` | Donors see the name, how long it takes and where it is held. | පරිත්‍යාගශීලීන්ට නම, ගත වන කාලය සහ පවත්වන ස්ථානය පෙනේ. | நன்கொடையாளர்கள் பெயர், எடுக்கும் நேரம், நடைபெறும் இடம் ஆகியவற்றைப் பார்ப்பார்கள். | | |
| `Services.types.form.name` | Name | නම | பெயர் | | |
| `Services.types.form.nameHint` | As donors know it, in English. | පරිත්‍යාගශීලීන් දන්නා ලෙස, ඉංග්‍රීසියෙන්. | நன்கொடையாளர்கள் அறிந்தபடி, ஆங்கிலத்தில். | | |
| `Services.types.form.nameTaken` | You already offer a service with that name. | ඔබ දැනටමත් එම නමින් සේවාවක් ලබා දෙයි. | அந்தப் பெயரில் ஏற்கனவே ஒரு சேவையை வழங்குகிறீர்கள். | | |
| `Services.types.form.nameSi` | Name in Sinhala | සිංහල නම | சிங்களப் பெயர் | | |
| `Services.types.form.nameTa` | Name in Tamil | දෙමළ නම | தமிழ்ப் பெயர் | | |
| `Services.types.form.descriptionLabel` | Description (optional) | විස්තරය (අත්‍යවශ්‍ය නොවේ) | விவரம் (விருப்பத்தேர்வு) | | |
| `Services.types.form.descriptionHint` | A line for donors: what it involves. | පරිත්‍යාගශීලීන් සඳහා පේළියක්: එයට අයත් දේ. | நன்கொடையாளர்களுக்கு ஒரு வரி: இதில் என்ன அடங்கும். | | |
| `Services.types.form.duration` | How long it takes | ගත වන කාලය | எடுக்கும் நேரம் | | |
| `Services.types.form.leadersRequired` | Leaders needed | අවශ්‍ය නායකයින් | தேவையான தலைவர்கள் | | |
| `Services.types.form.leadersHint` | From 1 to 20. | 1 සිට 20 දක්වා. | 1 முதல் 20 வரை. | | |
| `Services.types.form.where` | Where it is held | පවත්වන ස්ථානය | நடைபெறும் இடம் | | |
| `Services.types.form.buffer` | Travel time each way | එක් පැත්තකට ගමන් කාලය | ஒரு வழிக்கான பயண நேரம் | | |
| `Services.types.form.bufferHint` | Kept free before and after a visit to a donor's home, so leaders are not sent across town back to back. | පරිත්‍යාගශීලියෙකුගේ නිවසට යාමට පෙර හා පසු නිදහස්ව තබයි, එවිට නායකයින් නගරය හරහා එක දිගට යවනු නොලැබේ. | நன்கொடையாளரின் வீட்டுக்குச் செல்வதற்கு முன்னும் பின்னும் விடுபட்டிருக்கும்; தலைவர்கள் நகரம் முழுவதும் தொடர்ந்து அனுப்பப்பட மாட்டார்கள். | | |
| `Services.types.form.noBuffer` | None | නැත | இல்லை | | |
| `Services.types.form.minAdvance` | At least, days ahead | අවම වශයෙන්, දින කීයකට පෙර | குறைந்தது, எத்தனை நாட்கள் முன்பு | | |
| `Services.types.form.minAdvanceHint` | 0 lets donors ask for the same day. | 0 නම් එදිනම ඉල්ලිය හැක. | 0 என்றால் அதே நாளிலும் கோரலாம். | | |
| `Services.types.form.maxAdvance` | At most, days ahead | උපරිම වශයෙන්, දින කීයකට පෙර | அதிகபட்சம், எத்தனை நாட்கள் முன்பு | | |
| `Services.types.form.maxAdvanceHint` | How far ahead donors may ask. | පරිත්‍යාගශීලීන්ට කොපමණ කලින් ඉල්ලිය හැකිද. | நன்கொடையாளர்கள் எவ்வளவு முன்பே கோரலாம். | | |
| `Services.types.form.fixFields` | Please fix the fields marked above. | කරුණාකර ඉහත සලකුණු කළ ක්ෂේත්‍ර නිවැරදි කරන්න. | மேலே குறிக்கப்பட்ட புலங்களைத் திருத்துங்கள். | | |
| `Services.types.form.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Services.types.form.save` | Save | සුරකින්න | சேமி | | |
| `Services.types.form.create` | Add service | සේවාව එක් කරන්න | சேவையைச் சேருங்கள் | | |
| `Services.types.form.errors.name` | Please give a name of up to 100 characters. | කරුණාකර අක්ෂර 100ක් දක්වා නමක් දෙන්න. | 100 எழுத்துகள் வரையான ஒரு பெயரைத் தாருங்கள். | | |
| `Services.types.form.errors.nameSi` | Please keep this under 100 characters. | කරුණාකර අක්ෂර 100කට අඩුවෙන් තබන්න. | இதை 100 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.types.form.errors.nameTa` | Please keep this under 100 characters. | කරුණාකර අක්ෂර 100කට අඩුවෙන් තබන්න. | இதை 100 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.types.form.errors.description` | Please keep this under 300 characters. | කරුණාකර අක්ෂර 300කට අඩුවෙන් තබන්න. | இதை 300 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.types.form.errors.durationMinutes` | Please choose a length from 15 minutes to 12 hours. | කරුණාකර විනාඩි 15 සිට පැය 12 දක්වා කාලයක් තෝරන්න. | 15 நிமிடங்கள் முதல் 12 மணி நேரம் வரை தேர்ந்தெடுங்கள். | | |
| `Services.types.form.errors.bufferMinutes` | Please choose up to 3 hours. | කරුණාකර පැය 3ක් දක්වා තෝරන්න. | 3 மணி நேரம் வரை தேர்ந்தெடுங்கள். | | |
| `Services.types.form.errors.leadersRequired` | Please enter a number from 1 to 20. | කරුණාකර 1 සිට 20 දක්වා අංකයක් දෙන්න. | 1 முதல் 20 வரையான ஓர் எண்ணைத் தாருங்கள். | | |
| `Services.types.form.errors.locationMode` | Please choose where it is held. | කරුණාකර පවත්වන ස්ථානය තෝරන්න. | நடைபெறும் இடத்தைத் தேர்ந்தெடுங்கள். | | |
| `Services.types.form.errors.minAdvanceDays` | Please enter 0 or more days. | කරුණාකර දින 0ක් හෝ වැඩියෙන් දෙන්න. | 0 அல்லது அதற்கு மேற்பட்ட நாட்களைத் தாருங்கள். | | |
| `Services.types.form.errors.maxAdvanceDays` | Please enter a number no smaller than the first. | කරුණාකර පළමු අංකයට වඩා අඩු නොවන අංකයක් දෙන්න. | முதல் எண்ணைவிடக் குறையாத ஓர் எண்ணைத் தாருங்கள். | | |
| `Services.leaders.title` | Religious leaders | ආගමික නායකයින් | சமயத் தலைவர்கள் | | |
| `Services.leaders.intro` | The monks, priests, kurukkal and moulavis who lead your services: how to reach them, their usual week and their leave. A leader need not have an account. | ඔබගේ සේවා මෙහෙයවන හාමුදුරුවරු, පූජකවරු, කුරුක්කල්වරු සහ මවුලවිවරු: ඔවුන් සම්බන්ධ කර ගන්නා ආකාරය, සාමාන්‍ය සතිය සහ නිවාඩු. නායකයෙකුට ගිණුමක් අවශ්‍ය නැත. | உங்கள் சேவைகளை நடத்தும் பிக்குகள், குருமார்கள், குருக்கள்மார், மௌலவிகள்: அவர்களைத் தொடர்புகொள்ளும் வழி, வழக்கமான வாரம், விடுப்பு. தலைவருக்குக் கணக்கு தேவையில்லை. | | |
| `Services.leaders.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Services.leaders.stillHolds` | Still assigned to: {appointments}. Reassign these on the appointments screen. | තවමත් පවරා ඇත: {appointments}. සේවා වාර තිරයේ මේවා වෙනත් අයට පවරන්න. | இன்னும் நியமிக்கப்பட்டுள்ளது: {appointments}. நியமனங்கள் திரையில் இவற்றை வேறொருவருக்கு நியமியுங்கள். | | |
| `Services.leaders.dismiss` | Got it | හරි | சரி | | |
| `Services.leaders.heading` | Roster | ලැයිස්තුව | பட்டியல் | | |
| `Services.leaders.add` | Add a leader | නායකයෙකු එක් කරන්න | ஒரு தலைவரைச் சேருங்கள் | | |
| `Services.leaders.none` | No leaders on your roster yet. | ඔබගේ ලැයිස්තුවේ තවම නායකයින් නැත. | உங்கள் பட்டியலில் இன்னும் தலைவர்கள் இல்லை. | | |
| `Services.leaders.offRoster` | Off the roster | ලැයිස්තුවෙන් ඉවත් | பட்டியலில் இல்லை | | |
| `Services.leaders.noPhone` | No phone | දුරකථනයක් නැත | தொலைபேசி இல்லை | | |
| `Services.leaders.language.si` | Sinhala | සිංහල | சிங்களம் | | |
| `Services.leaders.language.ta` | Tamil | දෙමළ | தமிழ் | | |
| `Services.leaders.language.en` | English | ඉංග්‍රීසි | ஆங்கிலம் | | |
| `Services.leaders.signsIn` | Signs in | පුරනය වේ | உள்நுழைகிறார் | | |
| `Services.leaders.noAccount` | No account | ගිණුමක් නැත | கணக்கு இல்லை | | |
| `Services.leaders.noWeek` | No usual week recorded | සාමාන්‍ය සතියක් වාර්තා කර නැත | வழக்கமான வாரம் பதிவில் இல்லை | | |
| `Services.leaders.edit` | Edit | සංස්කරණය | திருத்துங்கள் | | |
| `Services.leaders.week` | Usual week | සාමාන්‍ය සතිය | வழக்கமான வாரம் | | |
| `Services.leaders.deactivate` | Take off roster | ලැයිස්තුවෙන් ඉවත් කරන්න | பட்டியலிலிருந்து நீக்குங்கள் | | |
| `Services.leaders.activate` | Put back on roster | නැවත ලැයිස්තුවට | மீண்டும் பட்டியலில் சேருங்கள் | | |
| `Services.leaders.leaveHeading` | Leave | නිවාඩු | விடுப்பு | | |
| `Services.leaders.addLeave` | Add leave | නිවාඩුවක් එක් කරන්න | விடுப்பைச் சேருங்கள் | | |
| `Services.leaders.noLeave` | No leave coming up. | ඉදිරියට නිවාඩු නැත. | வரவிருக்கும் விடுப்பு இல்லை. | | |
| `Services.leaders.removeLeave` | Remove | ඉවත් කරන්න | நீக்குங்கள் | | |
| `Services.leaders.form.newTitle` | Add a leader | නායකයෙකු එක් කරන්න | ஒரு தலைவரைச் சேருங்கள் | | |
| `Services.leaders.form.editTitle` | Edit leader | නායකයා සංස්කරණය | தலைவரைத் திருத்துங்கள் | | |
| `Services.leaders.form.description` | Texts about their services go to this phone, in this language. | ඔවුන්ගේ සේවා පිළිබඳ කෙටි පණිවිඩ මෙම දුරකථනයට, මෙම භාෂාවෙන් යැවේ. | அவர்களின் சேவைகள் பற்றிய குறுஞ்செய்திகள் இந்தத் தொலைபேசிக்கு, இந்த மொழியில் அனுப்பப்படும். | | |
| `Services.leaders.form.title` | Title | ගෞරව නාමය | பட்டம் | | |
| `Services.leaders.form.titleHint` | Ven., Rev., Fr., Moulavi... | පූජ්‍ය, ගරු, පියතුමා, මවුලවි... | வண., அருட்திரு, அருட்தந்தை, மௌலவி... | | |
| `Services.leaders.form.displayName` | Name | නම | பெயர் | | |
| `Services.leaders.form.nameSi` | Name in Sinhala | සිංහල නම | சிங்களப் பெயர் | | |
| `Services.leaders.form.nameTa` | Name in Tamil | දෙමළ නම | தமிழ்ப் பெயர் | | |
| `Services.leaders.form.phone` | Phone (optional) | දුරකථනය (අත්‍යවශ්‍ය නොවේ) | தொலைபேசி (விருப்பத்தேர்வு) | | |
| `Services.leaders.form.phoneHint` | Assignments and reminders are texted here. | පැවරීම් සහ සිහිකැඳවීම් මෙයට කෙටි පණිවිඩයෙන් යැවේ. | நியமனங்களும் நினைவூட்டல்களும் இங்கே குறுஞ்செய்தியாக அனுப்பப்படும். | | |
| `Services.leaders.form.language` | Texts in | පණිවිඩ භාෂාව | குறுஞ்செய்தி மொழி | | |
| `Services.leaders.form.languageHint` | If they sign in, their account's language is used instead. | ඔවුන් පුරනය වන්නේ නම්, ඔවුන්ගේ ගිණුමේ භාෂාව භාවිත වේ. | அவர்கள் உள்நுழைந்தால், அவர்களின் கணக்கின் மொழி பயன்படுத்தப்படும். | | |
| `Services.leaders.form.linkAccount` | Signs in to Sethika | Sethika වෙත පුරනය වේ | Sethika-வில் உள்நுழைகிறார் | | |
| `Services.leaders.form.linkAccountHint` | Links the account with this phone number, so they can answer from their own schedule. | මෙම දුරකථන අංකයේ ගිණුම සම්බන්ධ කරයි, එවිට ඔවුන්ට තමන්ගේම කාලසටහනෙන් පිළිතුරු දිය හැක. | இந்தத் தொலைபேசி எண்ணின் கணக்கை இணைக்கும்; அவர்கள் தங்கள் சொந்த அட்டவணையிலிருந்து பதிலளிக்கலாம். | | |
| `Services.leaders.form.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Services.leaders.form.save` | Save | සුරකින්න | சேமி | | |
| `Services.leaders.form.create` | Add leader | නායකයා එක් කරන්න | தலைவரைச் சேருங்கள் | | |
| `Services.leaders.form.errors.displayName` | Please give a name of up to 150 characters. | කරුණාකර අක්ෂර 150ක් දක්වා නමක් දෙන්න. | 150 எழுத்துகள் வரையான ஒரு பெயரைத் தாருங்கள். | | |
| `Services.leaders.form.errors.nameSi` | Please keep this under 150 characters. | කරුණාකර අක්ෂර 150කට අඩුවෙන් තබන්න. | இதை 150 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.leaders.form.errors.nameTa` | Please keep this under 150 characters. | කරුණාකර අක්ෂර 150කට අඩුවෙන් තබන්න. | இதை 150 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.leaders.form.errors.title` | Please keep this under 40 characters. | කරුණාකර අක්ෂර 40කට අඩුවෙන් තබන්න. | இதை 40 எழுத்துகளுக்குள் வையுங்கள். | | |
| `Services.leaders.form.errors.phone` | Please give a Sri Lankan mobile number; one is needed to link an account. | කරුණාකර ශ්‍රී ලංකා ජංගම අංකයක් දෙන්න; ගිණුමක් සම්බන්ධ කිරීමට එය අවශ්‍යයි. | இலங்கைக் கைபேசி எண்ணைத் தாருங்கள்; கணக்கை இணைக்க அது தேவை. | | |
| `Services.leaders.form.errors.language` | Please choose a language. | කරුණාකර භාෂාවක් තෝරන්න. | ஒரு மொழியைத் தேர்ந்தெடுங்கள். | | |
| `Services.leaders.weekSheet.title` | {leader}: usual week | {leader}: සාමාන්‍ය සතිය | {leader}: வழக்கமான வாரம் | | |
| `Services.leaders.weekSheet.description` | When they are normally available. Only a guide when assigning: it never stops an assignment. | ඔවුන් සාමාන්‍යයෙන් නිදහස් වේලාවන්. පැවරීමේදී මඟපෙන්වීමක් පමණි: එය කිසි විටෙක පැවරීමක් නවත්වන්නේ නැත. | அவர்கள் வழக்கமாகக் கிடைக்கும் நேரங்கள். நியமிக்கும்போது ஒரு வழிகாட்டல் மட்டுமே: இது ஒருபோதும் நியமனத்தைத் தடுக்காது. | | |
| `Services.leaders.weekSheet.none` | No times recorded. | වේලාවන් වාර්තා කර නැත. | நேரங்கள் பதிவில் இல்லை. | | |
| `Services.leaders.weekSheet.day` | Day | දිනය | நாள் | | |
| `Services.leaders.weekSheet.from` | From | සිට | முதல் | | |
| `Services.leaders.weekSheet.to` | To | දක්වා | வரை | | |
| `Services.leaders.weekSheet.remove` | Remove this time | මෙම වේලාව ඉවත් කරන්න | இந்த நேரத்தை நீக்குங்கள் | | |
| `Services.leaders.weekSheet.addSpan` | Add a time | වේලාවක් එක් කරන්න | ஒரு நேரத்தைச் சேருங்கள் | | |
| `Services.leaders.weekSheet.invalid` | Each time must end after it starts, and two times on one day must not overlap. | සෑම වේලාවක්ම ආරම්භයට පසු අවසන් විය යුතු අතර, එකම දිනයේ වේලාවන් දෙකක් එකිනෙක ගැටිය නොයුතුය. | ஒவ்வொரு நேரமும் தொடங்கிய பின்னரே முடிய வேண்டும்; ஒரே நாளின் இரு நேரங்கள் ஒன்றின்மேல் ஒன்று வரக்கூடாது. | | |
| `Services.leaders.weekSheet.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Services.leaders.weekSheet.save` | Save week | සතිය සුරකින්න | வாரத்தைச் சேமி | | |
| `Services.leaders.leaveSheet.title` | Add leave | නිවාඩුවක් එක් කරන්න | விடுப்பைச் சேருங்கள் | | |
| `Services.leaders.leaveSheet.description` | The leader cannot be assigned during leave. At most 31 days at a time. | නිවාඩු කාලයේ නායකයා පැවරිය නොහැක. වරකට උපරිම දින 31කි. | விடுப்பின்போது தலைவரை நியமிக்க முடியாது. ஒரு முறைக்கு அதிகபட்சம் 31 நாட்கள். | | |
| `Services.leaders.leaveSheet.leader` | Leader | නායකයා | தலைவர் | | |
| `Services.leaders.leaveSheet.from` | From | සිට | முதல் | | |
| `Services.leaders.leaveSheet.fromTime` | Time | වේලාව | நேரம் | | |
| `Services.leaders.leaveSheet.to` | Until | දක්වා | வரை | | |
| `Services.leaders.leaveSheet.toTime` | Time | වේලාව | நேரம் | | |
| `Services.leaders.leaveSheet.invalidFrom` | Please choose a start on the quarter hour. | කරුණාකර විනාඩි 15 පරතරයක ආරම්භයක් තෝරන්න. | கால் மணி இடைவெளியில் ஒரு தொடக்கத்தைத் தேர்ந்தெடுங்கள். | | |
| `Services.leaders.leaveSheet.invalidTo` | Please choose an end after the start, at most 31 days later, not already past. | කරුණාකර ආරම්භයට පසු, දින 31ක් ඇතුළත, තවම ගත නොවූ අවසානයක් තෝරන්න. | தொடக்கத்துக்குப் பின், 31 நாட்களுக்குள், இன்னும் கடந்துபோகாத ஒரு முடிவைத் தேர்ந்தெடுங்கள். | | |
| `Services.leaders.leaveSheet.reason` | Reason (optional) | හේතුව (අත්‍යවශ්‍ය නොවේ) | காரணம் (விருப்பத்தேர்வு) | | |
| `Services.leaders.leaveSheet.reasonHint` | Only your staff see it. | එය පෙනෙන්නේ ඔබගේ කාර්ය මණ්ඩලයට පමණි. | உங்கள் பணியாளர்கள் மட்டுமே இதைப் பார்ப்பார்கள். | | |
| `Services.leaders.leaveSheet.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Services.leaders.leaveSheet.add` | Add leave | නිවාඩුව එක් කරන්න | விடுப்பைச் சேருங்கள் | | |
| `Services.agenda.title` | Service appointments | සේවා වාර | சேவை நியமனங்கள் | | |
| `Services.agenda.intro` | Requests from donors, day by day: assign leaders to accept one, and follow their answers. | පරිත්‍යාගශීලීන්ගේ ඉල්ලීම්, දිනෙන් දින: ඉල්ලීමක් පිළිගැනීමට නායකයින් පවරා, ඔවුන්ගේ පිළිතුරු බලන්න. | நன்கொடையாளர்களின் கோரிக்கைகள், நாள்தோறும்: ஒன்றை ஏற்கத் தலைவர்களை நியமித்து, அவர்களின் பதில்களைப் பின்தொடருங்கள். | | |
| `Services.agenda.viewLabel` | Show | පෙන්වන්න | காட்டு | | |
| `Services.agenda.views.upcoming` | Coming up | ඉදිරියට | வரவிருப்பவை | | |
| `Services.agenda.views.waiting` | Waiting for you | ඔබ බලාපොරොත්තුවෙන් | உங்களுக்காகக் காத்திருப்பவை | | |
| `Services.agenda.views.needs-leaders` | Needs leaders | නායකයින් අවශ්‍යයි | தலைவர்கள் தேவை | | |
| `Services.agenda.views.history` | Earlier | පෙර | முந்தையவை | | |
| `Services.agenda.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Services.agenda.empty.upcoming` | Nothing coming up. | ඉදිරියට කිසිවක් නැත. | வரவிருப்பவை எதுவும் இல்லை. | | |
| `Services.agenda.empty.waiting` | No requests waiting. | බලාපොරොත්තුවෙන් ඉල්ලීම් නැත. | காத்திருக்கும் கோரிக்கைகள் இல்லை. | | |
| `Services.agenda.empty.needs-leaders` | Every appointment has its leaders. | සෑම සේවා වාරයකටම නායකයින් ඇත. | ஒவ்வொரு நியமனத்துக்கும் தலைவர்கள் உள்ளனர். | | |
| `Services.agenda.empty.history` | Nothing yet. | තවම කිසිවක් නැත. | இன்னும் எதுவும் இல்லை. | | |
| `Services.agenda.assigned` | Leaders assigned to {appointmentNo}. | {appointmentNo} වෙත නායකයින් පැවරුවා. | {appointmentNo} க்குத் தலைவர்கள் நியமிக்கப்பட்டனர். | | |
| `Services.agenda.needs` | {count, plural, one {Needs # more leader} other {Needs # more leaders}} | {count, plural, one {තවත් නායකයින් #ක් අවශ්‍යයි} other {තවත් නායකයින් #ක් අවශ්‍යයි}} | {count, plural, one {இன்னும் # தலைவர் தேவை} other {இன்னும் # தலைவர்கள் தேவை}} | | |
| `Services.agenda.atDonor` | At the donor's home: {address} | පරිත්‍යාගශීලියාගේ නිවසේදී: {address} | நன்கொடையாளரின் வீட்டில்: {address} | | |
| `Services.agenda.atInstitution` | Here, at the institution | මෙහිදී, ආයතනයේදී | இங்கே, நிறுவனத்தில் | | |
| `Services.agenda.rejectedBecause` | Not accepted: {reason} | පිළිගත්තේ නැත: {reason} | ஏற்கப்படவில்லை: {reason} | | |
| `Services.agenda.cancelledBecause` | Cancelled: {reason} | අවලංගු කළා: {reason} | ரத்து செய்யப்பட்டது: {reason} | | |
| `Services.agenda.response.Pending` | Not answered yet | තවම පිළිතුරු දී නැත | இன்னும் பதில் இல்லை | | |
| `Services.agenda.response.Accepted` | Accepted | පිළිගත්තා | ஏற்றுக்கொண்டார் | | |
| `Services.agenda.response.Declined` | Declined | ප්‍රතික්ෂේප කළා | மறுத்தார் | | |
| `Services.agenda.onBehalf` | answered for them | ඔවුන් වෙනුවෙන් පිළිතුරු දුන්නා | அவர் சார்பில் பதிலளிக்கப்பட்டது | | |
| `Services.agenda.acceptFor` | Accept for them | ඔවුන් වෙනුවෙන් පිළිගන්න | அவர் சார்பில் ஏற்றுக்கொள்ளுங்கள் | | |
| `Services.agenda.acceptedFor` | Accepted for {leader}. | {leader} වෙනුවෙන් පිළිගත්තා. | {leader} சார்பில் ஏற்றுக்கொள்ளப்பட்டது. | | |
| `Services.agenda.declineFor` | Decline for them | ඔවුන් වෙනුවෙන් ප්‍රතික්ෂේප කරන්න | அவர் சார்பில் மறுங்கள் | | |
| `Services.agenda.remove` | Remove | ඉවත් කරන්න | நீக்குங்கள் | | |
| `Services.agenda.removed` | {leader} was taken off. | {leader} ඉවත් කළා. | {leader} நீக்கப்பட்டார். | | |
| `Services.agenda.assignApprove` | Assign leaders and accept | නායකයින් පවරා පිළිගන්න | தலைவர்களை நியமித்து ஏற்றுக்கொள்ளுங்கள் | | |
| `Services.agenda.assignMore` | Assign leaders | නායකයින් පවරන්න | தலைவர்களை நியமியுங்கள் | | |
| `Services.agenda.reject` | Reject | ප්‍රතික්ෂේප කරන්න | நிராகரியுங்கள் | | |
| `Services.agenda.complete` | Mark as held | පැවැත්වූ බව සලකුණු කරන්න | நடைபெற்றதாகக் குறியுங்கள் | | |
| `Services.agenda.completed` | {appointmentNo} is marked as held. | {appointmentNo} පැවැත්වූ බව සලකුණු කළා. | {appointmentNo} நடைபெற்றதாகக் குறிக்கப்பட்டது. | | |
| `Services.agenda.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Services.agenda.assignSheet.title` | Assign leaders | නායකයින් පවරන්න | தலைவர்களை நியமியுங்கள் | | |
| `Services.agenda.assignSheet.description` | {appointmentNo}. {count, plural, one {Choose # more leader.} other {Choose up to # leaders.}} Each is texted and asked to accept. | {appointmentNo}. {count, plural, one {තවත් නායකයින් #ක් තෝරන්න.} other {නායකයින් #ක් දක්වා තෝරන්න.}} එක් එක් අයට කෙටි පණිවිඩයක් යවා පිළිගැනීමට ඉල්ලනු ලැබේ. | {appointmentNo}. {count, plural, one {இன்னும் # தலைவரைத் தேர்ந்தெடுங்கள்.} other {# தலைவர்கள் வரை தேர்ந்தெடுங்கள்.}} ஒவ்வொருவருக்கும் குறுஞ்செய்தி அனுப்பி ஏற்கக் கேட்கப்படும். | | |
| `Services.agenda.assignSheet.none` | No leaders on the roster yet. | ලැයිස්තුවේ තවම නායකයින් නැත. | பட்டியலில் இன்னும் தலைவர்கள் இல்லை. | | |
| `Services.agenda.assignSheet.assigned` | Already assigned | දැනටමත් පවරා ඇත | ஏற்கனவே நியமிக்கப்பட்டுள்ளார் | | |
| `Services.agenda.assignSheet.onLeave` | On leave then | එම කාලයේ නිවාඩුවේ | அப்போது விடுப்பில் | | |
| `Services.agenda.assignSheet.elsewhere` | At another service then | එම කාලයේ වෙනත් සේවාවක | அப்போது வேறொரு சேவையில் | | |
| `Services.agenda.assignSheet.outsideWeek` | Free, but outside their usual week | නිදහස්, නමුත් සාමාන්‍ය සතියෙන් පිටත | கிடைப்பார், ஆனால் வழக்கமான வாரத்துக்கு வெளியே | | |
| `Services.agenda.assignSheet.free` | Free | නිදහස් | கிடைப்பார் | | |
| `Services.agenda.assignSheet.cancel` | Cancel | අවලංගු කරන්න | ரத்துசெய் | | |
| `Services.agenda.assignSheet.assign` | {count, plural, =0 {Assign} one {Assign # leader} other {Assign # leaders}} | {count, plural, =0 {පවරන්න} one {නායකයින් # පවරන්න} other {නායකයින් # පවරන්න}} | {count, plural, =0 {நியமியுங்கள்} one {# தலைவரை நியமியுங்கள்} other {# தலைவர்களை நியமியுங்கள்}} | | |
| `Services.agenda.reasonSheet.reason` | Reason | හේතුව | காரணம் | | |
| `Services.agenda.reasonSheet.reasonOptional` | Reason (optional) | හේතුව (අත්‍යවශ්‍ය නොවේ) | காரணம் (விருப்பத்தேர்வு) | | |
| `Services.agenda.reasonSheet.reasonRequired` | Please give a reason. | කරුණාකර හේතුවක් දෙන්න. | காரணத்தைத் தாருங்கள். | | |
| `Services.agenda.reasonSheet.keep` | Go back | ආපසු | திரும்பு | | |
| `Services.agenda.reasonSheet.reject.title` | Reject this request? | මෙම ඉල්ලීම ප්‍රතික්ෂේප කරන්නද? | இந்தக் கோரிக்கையை நிராகரிக்கவா? | | |
| `Services.agenda.reasonSheet.reject.description` | {appointmentNo}. The donor is texted, and sees your reason when they sign in. | {appointmentNo}. පරිත්‍යාගශීලියාට කෙටි පණිවිඩයක් යැවෙන අතර, පුරනය වූ විට ඔබගේ හේතුව පෙනේ. | {appointmentNo}. நன்கொடையாளருக்குக் குறுஞ்செய்தி அனுப்பப்படும்; உள்நுழையும்போது உங்கள் காரணத்தைப் பார்ப்பார். | | |
| `Services.agenda.reasonSheet.reject.hint` | Written for the donor. | පරිත්‍යාගශීලියා සඳහා ලියන්න. | நன்கொடையாளருக்காக எழுதுங்கள். | | |
| `Services.agenda.reasonSheet.reject.confirm` | Reject request | ඉල්ලීම ප්‍රතික්ෂේප කරන්න | கோரிக்கையை நிராகரியுங்கள் | | |
| `Services.agenda.reasonSheet.reject.done` | {appointmentNo} was rejected. | {appointmentNo} ප්‍රතික්ෂේප කළා. | {appointmentNo} நிராகரிக்கப்பட்டது. | | |
| `Services.agenda.reasonSheet.cancel.title` | Cancel this appointment? | මෙම සේවා වාරය අවලංගු කරන්නද? | இந்த நியமனத்தை ரத்து செய்யவா? | | |
| `Services.agenda.reasonSheet.cancel.description` | {appointmentNo}. The donor and every leader still assigned are texted. | {appointmentNo}. පරිත්‍යාගශීලියාට සහ තවමත් පවරා ඇති සෑම නායකයෙකුටම කෙටි පණිවිඩ යැවේ. | {appointmentNo}. நன்கொடையாளருக்கும் இன்னும் நியமிக்கப்பட்டுள்ள ஒவ்வொரு தலைவருக்கும் குறுஞ்செய்தி அனுப்பப்படும். | | |
| `Services.agenda.reasonSheet.cancel.hint` | Written for the donor. | පරිත්‍යාගශීලියා සඳහා ලියන්න. | நன்கொடையாளருக்காக எழுதுங்கள். | | |
| `Services.agenda.reasonSheet.cancel.confirm` | Cancel appointment | සේවා වාරය අවලංගු කරන්න | நியமனத்தை ரத்து செய்யுங்கள் | | |
| `Services.agenda.reasonSheet.cancel.done` | {appointmentNo} was cancelled. | {appointmentNo} අවලංගු කළා. | {appointmentNo} ரத்து செய்யப்பட்டது. | | |
| `Services.agenda.reasonSheet.decline.title` | Decline for this leader? | මෙම නායකයා වෙනුවෙන් ප්‍රතික්ෂේප කරන්නද? | இந்தத் தலைவர் சார்பில் மறுக்கவா? | | |
| `Services.agenda.reasonSheet.decline.description` | {leader} cannot come to {appointmentNo}. Their time is freed, and the appointment needs another leader. | {leader} ට {appointmentNo} වෙත පැමිණිය නොහැක. ඔවුන්ගේ කාලය නිදහස් වන අතර, සේවා වාරයට වෙනත් නායකයෙකු අවශ්‍ය වේ. | {leader} அவர்களால் {appointmentNo} க்கு வர இயலாது. அவர்களின் நேரம் விடுவிக்கப்படும்; நியமனத்துக்கு வேறொரு தலைவர் தேவைப்படும். | | |
| `Services.agenda.reasonSheet.decline.hint` | For your own records. | ඔබගේම වාර්තා සඳහා. | உங்கள் சொந்தப் பதிவுக்காக. | | |
| `Services.agenda.reasonSheet.decline.confirm` | Decline for them | ඔවුන් වෙනුවෙන් ප්‍රතික්ෂේප කරන්න | அவர் சார்பில் மறுங்கள் | | |
| `Services.agenda.reasonSheet.decline.done` | The decline is recorded for {appointmentNo}. | {appointmentNo} සඳහා ප්‍රතික්ෂේපය වාර්තා කළා. | {appointmentNo} க்கான மறுப்பு பதிவுசெய்யப்பட்டது. | | |
| `Services.schedule.title` | My schedule | මගේ කාලසටහන | என் அட்டவணை | | |
| `Services.schedule.intro` | The services you are assigned to that are still to come. Please answer each, so the institution knows who is coming. | ඔබ පවරා ඇති, තවම ඉදිරියට ඇති සේවා. කවුරුන් පැමිණෙනවාදැයි ආයතනය දැන ගැනීමට, කරුණාකර එක් එක් එකට පිළිතුරු දෙන්න. | தாங்கள் நியமிக்கப்பட்டுள்ள, இனி வரவிருக்கும் சேவைகள். யார் வருகிறார்கள் என்று நிறுவனம் அறிய, ஒவ்வொன்றுக்கும் பதிலளியுங்கள். | | |
| `Services.schedule.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Services.schedule.none` | Nothing assigned to you yet. | ඔබට තවම කිසිවක් පවරා නැත. | தங்களுக்கு இன்னும் எதுவும் நியமிக்கப்படவில்லை. | | |
| `Services.schedule.response.Pending` | Please answer | කරුණාකර පිළිතුරු දෙන්න | பதிலளியுங்கள் | | |
| `Services.schedule.response.Accepted` | You accepted | පිළිගත්තා | ஏற்றுக்கொண்டீர்கள் | | |
| `Services.schedule.atDonor` | At the donor's home: {address} | පරිත්‍යාගශීලියාගේ නිවසේදී: {address} | நன்கொடையாளரின் வீட்டில்: {address} | | |
| `Services.schedule.atInstitution` | At the institution | ආයතනයේදී | நிறுவனத்தில் | | |
| `Services.schedule.accept` | Accept | පිළිගන්න | ஏற்றுக்கொள்ளுங்கள் | | |
| `Services.schedule.decline` | I cannot come | පැමිණිය නොහැක | வர இயலாது | | |
| `Services.schedule.acceptedToast` | You accepted {appointmentNo}. | {appointmentNo} පිළිගත්තා. | {appointmentNo} ஏற்றுக்கொள்ளப்பட்டது. | | |
| `Services.schedule.declinedToast` | The institution is told you cannot come to {appointmentNo}. | {appointmentNo} වෙත පැමිණිය නොහැකි බව ආයතනයට දැන්වුවා. | {appointmentNo} க்குத் தாங்கள் வர இயலாது என்று நிறுவனத்துக்கு அறிவிக்கப்பட்டது. | | |
| `Services.schedule.declineSheet.title` | Cannot come? | පැමිණිය නොහැකිද? | வர இயலாதா? | | |
| `Services.schedule.declineSheet.description` | {appointmentNo}. Your time is freed and the institution assigns someone else. | {appointmentNo}. ඔබගේ කාලය නිදහස් වන අතර ආයතනය වෙනත් අයෙකු පවරයි. | {appointmentNo}. தங்கள் நேரம் விடுவிக்கப்படும்; நிறுவனம் வேறொருவரை நியமிக்கும். | | |
| `Services.schedule.declineSheet.reason` | Reason (optional) | හේතුව (අත්‍යවශ්‍ය නොවේ) | காரணம் (விருப்பத்தேர்வு) | | |
| `Services.schedule.declineSheet.reasonHint` | The institution will see it. | ආයතනයට එය පෙනෙනු ඇත. | நிறுவனம் இதைப் பார்க்கும். | | |
| `Services.schedule.declineSheet.keep` | Go back | ආපසු | திரும்பு | | |
| `Services.schedule.declineSheet.confirm` | I cannot come | පැමිණිය නොහැක | வர இயலாது | | |
| `Shell.groups.request` | Religious services | ආගමික සේවා | சமயச் சேவைகள் | | |
| `Shell.groups.services` | Religious services | ආගමික සේවා | சமயச் சேவைகள் | | |
| `Shell.items.myServices` | My service requests | මගේ සේවා ඉල්ලීම් | என் சேவைக் கோரிக்கைகள் | | |
| `Shell.items.serviceTypes` | Services offered | ලබා දෙන සේවා | வழங்கப்படும் சேவைகள் | | |
| `Shell.items.leaders` | Religious leaders | ආගමික නායකයින් | சமயத் தலைவர்கள் | | |
| `Shell.items.appointments` | Service appointments | සේවා වාර | சேவை நியமனங்கள் | | |
| `Shell.items.schedule` | My schedule | මගේ කාලසටහන | என் அட்டவணை | | |

## Institution pages for search engines (2026-11-01, `Metadata`, plan 06 B.3)

The snippet a search engine shows under an institution's page when the institution wrote no
description of its own. Drafts, written by the developer; please check them. `{name}` and `{city}`
are filled in by the app.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Metadata.institution` | {name}, {city}. Sponsor a meal here through Sethika. | {name}, {city}. Sethika හරහා මෙහි ආහාර වේලකට අනුග්‍රහය දක්වන්න. | {name}, {city}. Sethika மூலம் இங்கு ஒரு வேளை உணவுக்கு அனுசரணை வழங்குங்கள். | | |

## Find a place to give (2026-11-01, `Find`, plan 06 B.4)

The search page: where to look (near me, a district, anywhere), the time of day, the kind of place, a
month of open days, the list and the map. Drafts, written by the developer; please check them.
`{...}` are filled in by the app; `#` in a plural is the number. The old directory's
`Discover.directoryIntro`, `filter`, `filterHint`, `noneListed` and `noneMatch` were removed with it.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Find.title` | Find a place to give | දීමට ස්ථානයක් සොයන්න | வழங்க ஓர் இடத்தைக் கண்டறியுங்கள் | | |
| `Find.intro` | Choose a day and where to look. Every place shown has a meal you can sponsor that day. | දිනයක් සහ සොයන ප්‍රදේශය තෝරන්න. මෙහි පෙන්වන සෑම ස්ථානයකම එදිනට ඔබට අනුග්‍රහය දැක්විය හැකි ආහාර වේලක් ඇත. | ஒரு நாளையும் தேட வேண்டிய இடத்தையும் தேர்ந்தெடுங்கள். காட்டப்படும் ஒவ்வோர் இடத்திலும் அன்று நீங்கள் அனுசரணை வழங்கக்கூடிய ஒரு வேளை உணவு உள்ளது. | | |
| `Find.where.label` | Where | කොහේද | எங்கே | | |
| `Find.where.near` | Near me | මා අසල | எனக்கு அருகில் | | |
| `Find.where.locating` | Finding you… | ඔබ සිටින තැන සොයමින්… | உங்கள் இருப்பிடத்தைக் கண்டறிகிறது… | | |
| `Find.where.district` | In a district | දිස්ත්‍රික්කයක | ஒரு மாவட்டத்தில் | | |
| `Find.where.anywhere` | Anywhere | ඕනෑම තැනක | எங்கும் | | |
| `Find.where.districtLabel` | District | දිස්ත්‍රික්කය | மாவட்டம் | | |
| `Find.where.chooseDistrict` | Choose a district | දිස්ත්‍රික්කයක් තෝරන්න | ஒரு மாவட்டத்தைத் தேர்ந்தெடுங்கள் | | |
| `Find.where.radius` | Within | දුර | தூரம் | | |
| `Find.where.km` | {km} km | කි.මී. {km} | {km} கி.மீ. | | |
| `Find.where.denied` | Location is turned off for Sethika. Allow it in your browser, or choose a district. | Sethika සඳහා ස්ථාන තොරතුරු අක්‍රීයයි. ඔබගේ බ්‍රවුසරයේ එයට ඉඩ දෙන්න, නැතහොත් දිස්ත්‍රික්කයක් තෝරන්න. | Sethika-க்கு இருப்பிட அனுமதி முடக்கப்பட்டுள்ளது. உங்கள் உலாவியில் அனுமதியுங்கள், அல்லது ஒரு மாவட்டத்தைத் தேர்ந்தெடுங்கள். | | |
| `Find.where.outside` | You seem to be outside Sri Lanka. Choose a district instead. | ඔබ ශ්‍රී ලංකාවෙන් පිටත සිටින බව පෙනේ. ඒ වෙනුවට දිස්ත්‍රික්කයක් තෝරන්න. | நீங்கள் இலங்கைக்கு வெளியே இருப்பதாகத் தெரிகிறது. அதற்குப் பதிலாக ஒரு மாவட்டத்தைத் தேர்ந்தெடுங்கள். | | |
| `Find.where.unavailable` | Your location could not be found. Try again, or choose a district. | ඔබගේ ස්ථානය සොයාගත නොහැකි විය. නැවත උත්සාහ කරන්න, නැතහොත් දිස්ත්‍රික්කයක් තෝරන්න. | உங்கள் இருப்பிடத்தைக் கண்டறிய முடியவில்லை. மீண்டும் முயலுங்கள், அல்லது ஒரு மாவட்டத்தைத் தேர்ந்தெடுங்கள். | | |
| `Find.time.label` | Time of day | දවසේ වේලාව | நாளின் நேரம் | | |
| `Find.time.any` | Any time | ඕනෑම වේලාවක | எந்த நேரமும் | | |
| `Find.time.morning` | Morning | උදෑසන | காலை | | |
| `Find.time.midday` | Midday | දහවල් | மதியம் | | |
| `Find.time.evening` | Evening | සවස | மாலை | | |
| `Find.type.label` | Kind of place | ස්ථාන වර්ගය | இடத்தின் வகை | | |
| `Find.type.all` | Every kind | සියලු වර්ග | எல்லா வகைகளும் | | |
| `Find.calendar.title` | Choose a day | දිනයක් තෝරන්න | ஒரு நாளைத் தேர்ந்தெடுங்கள் | | |
| `Find.calendar.hint` | Each day shows how many places have a meal open. | සෑම දිනකම ආහාර වේලක් විවෘත ස්ථාන ගණන පෙන්වයි. | ஒவ்வொரு நாளும் உணவு வேளை திறந்துள்ள இடங்களின் எண்ணிக்கையைக் காட்டுகிறது. | | |
| `Find.calendar.open` | {count, plural, =0 {No place open} one {# place open} other {# places open}} | {count, plural, =0 {විවෘත ස්ථාන නැත} one {ස්ථාන #ක් විවෘතයි} other {ස්ථාන #ක් විවෘතයි}} | {count, plural, =0 {திறந்த இடம் இல்லை} one {# இடம் திறந்துள்ளது} other {# இடங்கள் திறந்துள்ளன}} | | |
| `Find.tabs.label` | Show the places as | ස්ථාන පෙන්වන ආකාරය | இடங்களைக் காட்டும் விதம் | | |
| `Find.tabs.list` | List | ලැයිස්තුව | பட்டியல் | | |
| `Find.tabs.map` | Map | සිතියම | வரைபடம் | | |
| `Find.results.title` | Open on {date} | {date} දින විවෘත | {date} அன்று திறந்துள்ளவை | | |
| `Find.results.count` | {count, plural, =0 {No places found.} one {# place found.} other {# places found.}} | {count, plural, =0 {ස්ථාන කිසිවක් හමු නොවීය.} one {ස්ථාන #ක් හමු විය.} other {ස්ථාන #ක් හමු විය.}} | {count, plural, =0 {இடங்கள் எதுவும் கிடைக்கவில்லை.} one {# இடம் கிடைத்தது.} other {# இடங்கள் கிடைத்தன.}} | | |
| `Find.results.moreNearest` | These are the nearest 100. | මේ ළඟම ඇති ස්ථාන 100 යි. | இவை அருகிலுள்ள 100 இடங்கள். | | |
| `Find.results.moreByName` | These are the first 100 by name. | මේ නම අනුව පළමු ස්ථාන 100 යි. | இவை பெயர் வரிசையில் முதல் 100 இடங்கள். | | |
| `Find.results.none` | No place has a meal you can sponsor on {date} here. Try another day, another time of day or a wider area. | {date} දින මෙහි ඔබට අනුග්‍රහය දැක්විය හැකි ආහාර වේලක් ඇති ස්ථානයක් නැත. වෙනත් දිනයක්, වෙනත් වේලාවක් හෝ විශාල ප්‍රදේශයක් උත්සාහ කරන්න. | {date} அன்று இங்கு நீங்கள் அனுசரணை வழங்கக்கூடிய உணவு வேளை எந்த இடத்திலும் இல்லை. வேறு நாளையோ, வேறு நேரத்தையோ, பெரிய பகுதியையோ முயலுங்கள். | | |
| `Find.results.distance` | {km} km away | කි.මී. {km}ක් දුරින් | {km} கி.மீ. தொலைவில் | | |
| `Find.results.left` | {count, plural, one {# sponsor place left} other {# sponsor places left}} | {count, plural, one {අනුග්‍රාහක ස්ථාන #ක් ඉතිරියි} other {අනුග්‍රාහක ස්ථාන #ක් ඉතිරියි}} | {count, plural, one {# அனுசரணை இடம் மீதம்} other {# அனுசரணை இடங்கள் மீதம்}} | | |
| `Find.results.book` | Book | වෙන් කරන්න | முன்பதிவு | | |
| `Find.results.bookLabel` | Book {meal} at {place} | {place} හි {meal} වෙන් කරන්න | {place} இல் {meal} முன்பதிவு செய்யுங்கள் | | |
| `Find.results.failed` | The places could not be loaded. | ස්ථාන පූරණය කළ නොහැකි විය. | இடங்களை ஏற்ற முடியவில்லை. | | |
| `Find.results.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் | | |
| `Find.results.invalid` | This search cannot be run. | මෙම සෙවීම කළ නොහැක. | இந்தத் தேடலை இயக்க முடியாது. | | |
| `Find.results.startAgain` | Start again | මුල සිට අරඹන්න | மீண்டும் தொடங்குங்கள் | | |
| `Find.map.label` | Map of the places found | හමු වූ ස්ථානවල සිතියම | கண்டறியப்பட்ட இடங்களின் வரைபடம் | | |
| `Find.map.searchHere` | Search this area | මෙම ප්‍රදේශයේ සොයන්න | இந்தப் பகுதியில் தேடுங்கள் | | |
| `Find.map.outside` | This part of the map is outside Sri Lanka. | සිතියමේ මෙම කොටස ශ්‍රී ලංකාවෙන් පිටත ය. | வரைபடத்தின் இந்தப் பகுதி இலங்கைக்கு வெளியே உள்ளது. | | |

## A meal's cost on the server-rendered page (2026-11-01, `Discover.costPerHead`, plan 06 B.4)

The cost now carries its currency in the message (the number is filled in with plain digits), because the
browser cannot format Sinhala currency the way the server does. Drafts; please check the currency's abbreviation.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Discover.costPerHead` | About Rs {cost} a person | එක් අයෙකුට ආසන්න වශයෙන් රු. {cost} | ஒருவருக்குச் சுமார் ரூ. {cost} | | |

## Yearly booking texts (2026-11-01, `RECURRING_FULL`, `RECURRING_CLOSED`, plan 07 C.1)

Sent when the yearly job cannot make a year's booking. `{institution}`, `{date}` and `{time}` are filled in by
the app. Drafts, written by the developer; please check them, and keep each as short as it can be (SMS segments).

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `RECURRING_FULL` | Sethika: {institution}, {date} {time}. Your yearly booking could not be made: every place is taken. Sign in to choose another day. | Sethika: {institution}, {date} {time}. වාර්ෂික වෙන්කිරීම කළ නොහැකි විය: ස්ථාන සියල්ල වෙන් කර ඇත. වෙනත් දිනයකට පිවිසෙන්න. | Sethika: {institution}, {date} {time}. வருடாந்த முன்பதிவு முடியவில்லை: இடங்கள் நிரம்பின. வேறு நாளுக்கு உள்நுழையவும். | | |
| `RECURRING_CLOSED` | Sethika: {institution}, {date} {time}. Your yearly booking could not be made: the meal is not open that day. Sign in to choose another day. | Sethika: {institution}, {date} {time}. වාර්ෂික වෙන්කිරීම කළ නොහැකි විය: එදින මෙම ආහාර වේල නැත. වෙනත් දිනයකට පිවිසෙන්න. | Sethika: {institution}, {date} {time}. வருடாந்த முன்பதிவு முடியவில்லை: அன்று இந்த வேளை இல்லை. வேறு நாளுக்கு உள்நுழையவும். | | |

## Memorial Dana Planner (2026-11-01, `core.LookupValues`, `core.MemorialRules`, `MEMORIAL_REMINDER`, plan 07 C.2)

Seeded in `Scripts/Seed/13_MemorialDrafts.sql`. The religions' and relationships' Sinhala and Tamil
names are drafts, written by the developer. The observance rules are drafts twice over: the names, and
the custom itself (which days each faith keeps, and whether "the 7th day" counts the day of passing as
the first). Every rule's name starts with `DRAFT - ` in all three languages until a reviewer confirms it.

**Religions (`RELIGION`)**

| Code | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `BUDDHISM` | Buddhism | බෞද්ධ | பௌத்தம் | | |
| `HINDUISM` | Hinduism | හින්දු | இந்து | | |
| `ISLAM` | Islam | ඉස්ලාම් | இஸ்லாம் | | |
| `CHRISTIANITY` | Christianity | ක්‍රිස්තියානි | கிறிஸ்தவம் | | |

**Relationships (`RELATIONSHIP`)**

| Code | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `MOTHER` | Mother | මව | தாய் | | |
| `FATHER` | Father | පියා | தந்தை | | |
| `WIFE` | Wife | බිරිඳ | மனைவி | | |
| `HUSBAND` | Husband | සැමියා | கணவர் | | |
| `DAUGHTER` | Daughter | දියණිය | மகள் | | |
| `SON` | Son | පුතා | மகன் | | |
| `GRANDMOTHER` | Grandmother | ආච්චි | பாட்டி | | |
| `GRANDFATHER` | Grandfather | සීයා | தாத்தா | | |
| `SISTER` | Sister | සහෝදරිය | சகோதரி | | |
| `BROTHER` | Brother | සහෝදරයා | சகோதரர் | | |
| `RELATIVE` | Relative | ඥාතියා | உறவினர் | | |
| `FRIEND` | Friend | මිතුරා | நண்பர் | | |

**Observances (`core.MemorialRules`)**: the day is the date of passing plus the months, then the days.

| Code | Months, days | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|---|
| `BUDDHIST_7TH_DAY` | 0, 6 | DRAFT - 7th day almsgiving | DRAFT - සත්වන දින දානය | DRAFT - 7ஆம் நாள் தானம் | | |
| `BUDDHIST_3_MONTHS` | 3, 0 | DRAFT - 3 months almsgiving | DRAFT - තෙමසේ දානය | DRAFT - 3 மாத தானம் | | |
| `BUDDHIST_YEARLY` | 12, 0, every year | DRAFT - Yearly almsgiving | DRAFT - වාර්ෂික දානය | DRAFT - வருடாந்த தானம் | | |
| `HINDU_31ST_DAY` | 0, 30 | DRAFT - 31st day | DRAFT - 31 වන දිනය | DRAFT - 31ஆம் நாள் | | |
| `MUSLIM_40TH_DAY` | 0, 39 | DRAFT - 40th day | DRAFT - 40 වන දිනය | DRAFT - 40ஆம் நாள் | | |
| `CHRISTIAN_40TH_DAY` | 0, 39 | DRAFT - 40th day | DRAFT - 40 වන දිනය | DRAFT - 40ஆம் நாள் | | |

**The reminder text (`MEMORIAL_REMINDER`)**, sent a week before an observance with no booking.
`{date}` and `{observance}` (the rule's name in the donor's language) are filled in by the app. The name
of the person who died is never in the text.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `MEMORIAL_REMINDER` | Sethika: {date}, {observance}. A memorial day in your plan is near and not booked yet. Sign in to find a place to give. | Sethika: {date}, {observance}. ඔබේ සමරු දිනයක් ළඟයි, තවම වෙන් කර නැත. ස්ථානයක් සොයන්න පිවිසෙන්න. | Sethika: {date}, {observance}. உங்கள் நினைவு நாள் நெருங்குகிறது, முன்பதிவு இல்லை. இடம் தேட உள்நுழையவும். | | |

## Phase C screens (2026-11-02, `Memorial`, `Share`, `MyBookings`, `Discover`, `Booking.errors`, plan 07 C.5)

Every year in the booking sheet and My bookings, the memorial planner, sharing a booking and its link, the heart
and My places. Drafts by Claude; `{...}` are filled in by the app.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Shell.items.memorial` | Memorial dana planner | අනුස්මරණ දාන සැලසුම | நினைவு தான திட்டம் | | |
| `Dashboard.panels.myPlaces` | My places | මගේ ස්ථාන | என் இடங்கள் | | |
| `Dashboard.panels.noPlaces` | Tap the heart on a place's page to keep it here. | ස්ථානයක පිටුවේ හදවත ඔබා එය මෙහි තබා ගන්න. | ஒரு இடத்தின் பக்கத்தில் உள்ள இதயத்தைத் தட்டி அதை இங்கே வைத்துக்கொள்ளுங்கள். | | |
| `Discover.sheet.forMemorial` | This booking is for a memorial day in your planner. | මෙම වෙන්කිරීම ඔබේ සැලසුමේ අනුස්මරණ දිනයක් සඳහා ය. | இந்த முன்பதிவு உங்கள் திட்டத்தில் உள்ள ஒரு நினைவு நாளுக்கானது. | | |
| `Discover.sheet.everyYear` | Book this every year | සෑම වසරකම මෙය වෙන් කරන්න | இதை ஒவ்வொரு ஆண்டும் முன்பதிவு செய்யவும் | | |
| `Discover.sheet.everyYearHint` | We book the same meal on the same day each year, as soon as the day opens, and text you. You can stop it any time in My bookings. | දිනය විවෘත වූ විගස සෑම වසරකම එම දිනයේ එම ආහාර වේල අපි වෙන් කර ඔබට කෙටි පණිවිඩයක් එවන්නෙමු. මගේ වෙන්කිරීම් තුළ ඕනෑම වේලාවක එය නවත්වන්න පුළුවන්. | நாள் திறந்தவுடன் ஒவ்வொரு ஆண்டும் அதே நாளில் அதே உணவை நாங்கள் முன்பதிவு செய்து உங்களுக்குக் குறுஞ்செய்தி அனுப்புவோம். என் முன்பதிவுகளில் எப்போது வேண்டுமானாலும் நிறுத்தலாம். | | |
| `Discover.sheet.everyYearNotYearly` | This memorial day does not come every year, so it cannot be booked every year. | මෙම අනුස්මරණ දිනය සෑම වසරකම නොඑන නිසා එය සෑම වසරකම වෙන් කළ නොහැක. | இந்த நினைவு நாள் ஒவ்வொரு ஆண்டும் வருவதில்லை, எனவே ஒவ்வொரு ஆண்டும் முன்பதிவு செய்ய முடியாது. | | |
| `Discover.sheet.seePlan` | See my memorial plans | මගේ අනුස්මරණ සැලසුම් බලන්න | என் நினைவுத் திட்டங்களைப் பார்க்கவும் | | |
| `Discover.heart.on` | In my places | මගේ ස්ථානවල ඇත | என் இடங்களில் உள்ளது | | |
| `Discover.heart.off` | Keep this place | මෙම ස්ථානය තබා ගන්න | இந்த இடத்தை வைத்துக்கொள் | | |
| `Discover.heart.kept` | {name} is in My places. | {name} මගේ ස්ථානවලට එක් කළා. | {name} என் இடங்களில் சேர்க்கப்பட்டது. | | |
| `Discover.heart.letGo` | {name} is no longer in My places. | {name} මගේ ස්ථානවලින් ඉවත් කළා. | {name} என் இடங்களிலிருந்து நீக்கப்பட்டது. | | |
| `Discover.heart.failed` | That did not work. Please try again. | එය සාර්ථක වූයේ නැත. නැවත උත්සාහ කරන්න. | அது வேலை செய்யவில்லை. மீண்டும் முயற்சிக்கவும். | | |
| `Booking.errors.recurring_not_found` | This yearly booking has already been stopped. | මෙම වාර්ෂික වෙන්කිරීම දැනටමත් නවත්වා ඇත. | இந்த வருடாந்த முன்பதிவு ஏற்கனவே நிறுத்தப்பட்டுள்ளது. | | |
| `Booking.errors.memorial_not_found` | This memorial plan could not be found. It may have been archived. | මෙම අනුස්මරණ සැලසුම සොයාගත නොහැක. එය ඉවත් කර තිබිය හැක. | இந்த நினைவுத் திட்டம் கிடைக்கவில்லை. அது நீக்கப்பட்டிருக்கலாம். | | |
| `Booking.errors.memorial_plan_not_found` | This memorial plan could not be found. It may have been archived. | මෙම අනුස්මරණ සැලසුම සොයාගත නොහැක. එය ඉවත් කර තිබිය හැක. | இந்த நினைவுத் திட்டம் கிடைக்கவில்லை. அது நீக்கப்பட்டிருக்கலாம். | | |
| `Booking.errors.not_an_observance` | This is not the day of that memorial. Please book the day shown in your planner. | මෙය එම අනුස්මරණයේ දිනය නොවේ. ඔබේ සැලසුමේ පෙන්වන දිනය වෙන් කරන්න. | இது அந்த நினைவு நாள் அல்ல. உங்கள் திட்டத்தில் காட்டப்படும் நாளை முன்பதிவு செய்யவும். | | |
| `Booking.errors.booking_closed` | This booking can no longer be shared or joined. | මෙම වෙන්කිරීම තවදුරටත් බෙදා ගැනීමට හෝ එක්වීමට නොහැක. | இந்த முன்பதிவைப் இனி பகிரவோ சேரவோ முடியாது. | | |
| `Booking.errors.too_many_invitations` | A booking can be shared with at most 10 people. | එක් වෙන්කිරීමක් උපරිම 10 දෙනෙකු සමඟ බෙදා ගත හැක. | ஒரு முன்பதிவை அதிகபட்சம் 10 பேருடன் பகிரலாம். | | |
| `Booking.errors.invitation_not_found` | This invitation has already been withdrawn. | මෙම ආරාධනාව දැනටමත් ඉල්ලා අස්කර ඇත. | இந்த அழைப்பு ஏற்கனவே திரும்பப் பெறப்பட்டது. | | |
| `Booking.errors.invitation_answered` | This invitation has already been answered. | මෙම ආරාධනාවට දැනටමත් පිළිතුරු දී ඇත. | இந்த அழைப்புக்கு ஏற்கனவே பதில் அளிக்கப்பட்டது. | | |
| `Booking.errors.link_not_found` | This link is not valid, or the invitation was withdrawn. | මෙම සබැඳිය වලංගු නැත, නැතහොත් ආරාධනාව ඉල්ලා අස්කර ඇත. | இந்த இணைப்பு செல்லாது, அல்லது அழைப்பு திரும்பப் பெறப்பட்டது. | | |
| `Booking.errors.own_booking` | This is your own booking: you do not need to join it. | මෙය ඔබේම වෙන්කිරීමයි: ඔබට එයට එක්වීමට අවශ්‍ය නැත. | இது உங்கள் சொந்த முன்பதிவு: நீங்கள் சேர வேண்டியதில்லை. | | |
| `Booking.errors.already_joined` | You have already joined this booking. | ඔබ දැනටමත් මෙම වෙන්කිරීමට එක්වී ඇත. | நீங்கள் ஏற்கனவே இந்த முன்பதிவில் சேர்ந்துள்ளீர்கள். | | |
| `MyBookings.partOfYearly` | Booked every year | සෑම වසරකම වෙන් කෙරේ | ஒவ்வொரு ஆண்டும் முன்பதிவு | | |
| `MyBookings.shareThis` | Share this dana | මෙම දානය බෙදා ගන්න | இந்த தானத்தைப் பகிரவும் | | |
| `MyBookings.yearly.title` | Every year | සෑම වසරකම | ஒவ்வொரு ஆண்டும் | | |
| `MyBookings.yearly.intro` | Meals we book for you each year, as soon as the day opens. | දිනය විවෘත වූ විගස සෑම වසරකම අපි ඔබ වෙනුවෙන් වෙන් කරන ආහාර වේල්. | நாள் திறந்தவுடன் ஒவ்வொரு ஆண்டும் உங்களுக்காக நாங்கள் முன்பதிவு செய்யும் உணவுகள். | | |
| `MyBookings.yearly.every` | Every {day} | සෑම {day} | ஒவ்வொரு {day} | | |
| `MyBookings.yearly.next` | next on {date} | ඊළඟට {date} | அடுத்தது {date} | | |
| `MyBookings.yearly.ended` | no more years to book | තවත් වෙන් කිරීමට වසර නැත | இனி முன்பதிவு செய்ய ஆண்டுகள் இல்லை | | |
| `MyBookings.yearly.stop` | Stop booking every year | සෑම වසරකම වෙන් කිරීම නවත්වන්න | ஒவ்வொரு ஆண்டும் முன்பதிவு செய்வதை நிறுத்து | | |
| `MyBookings.yearly.stopTitle` | Stop booking this every year? | මෙය සෑම වසරකම වෙන් කිරීම නවත්වන්නද? | இதை ஒவ்வொரு ஆண்டும் முன்பதிவு செய்வதை நிறுத்தவா? | | |
| `MyBookings.yearly.stopExplain` | We will not book it in later years. A year already booked stays booked; cancel it above if you need to. | ඉදිරි වසරවල අපි එය වෙන් නොකරමු. දැනටමත් වෙන් කළ වසරක් එලෙසම පවතී; අවශ්‍ය නම් ඉහතින් එය අවලංගු කරන්න. | வரும் ஆண்டுகளில் நாங்கள் இதை முன்பதிவு செய்ய மாட்டோம். ஏற்கனவே முன்பதிவு செய்த ஆண்டு அப்படியே இருக்கும்; தேவைப்பட்டால் மேலே ரத்து செய்யவும். | | |
| `MyBookings.yearly.keep` | Keep it | තබා ගන්න | வைத்திரு | | |
| `MyBookings.yearly.stopConfirm` | Stop | නවත්වන්න | நிறுத்து | | |
| `MyBookings.yearly.stopped` | It will not be booked again. | එය නැවත වෙන් නොකෙරේ. | இது மீண்டும் முன்பதிவு செய்யப்படாது. | | |
| `MyBookings.shared.title` | Shared with me | මා සමඟ බෙදාගත් | என்னுடன் பகிரப்பட்டவை | | |
| `MyBookings.shared.with` | With {name} | {name} සමඟ | {name} உடன் | | |
| `MyBookings.share.title` | Share this dana | මෙම දානය බෙදා ගන්න | இந்த தானத்தைப் பகிரவும் | | |
| `MyBookings.share.description` | Invite family or friends to give this meal with you. We make a link; you send it to them yourself. | මෙම දානය ඔබ සමඟ දීමට පවුලේ අයට හෝ මිතුරන්ට ආරාධනා කරන්න. අපි සබැඳියක් සාදමු; ඔබ එය ඔවුන්ට යවන්න. | இந்த உணவை உங்களுடன் வழங்க குடும்பத்தினரை அல்லது நண்பர்களை அழைக்கவும். நாங்கள் ஒரு இணைப்பை உருவாக்குவோம்; நீங்களே அதை அவர்களுக்கு அனுப்புங்கள். | | |
| `MyBookings.share.full` | This booking is already shared with {max} people, the most it can be. | මෙම වෙන්කිරීම දැනටමත් උපරිමය වන {max} දෙනෙකු සමඟ බෙදාගෙන ඇත. | இந்த முன்பதிவு ஏற்கனவே அதிகபட்சமான {max} பேருடன் பகிரப்பட்டுள்ளது. | | |
| `MyBookings.share.invited` | Invited | ආරාධිතයන් | அழைக்கப்பட்டவர்கள் | | |
| `MyBookings.share.nobody` | Nobody yet. | තවම කිසිවෙක් නැත. | இன்னும் யாரும் இல்லை. | | |
| `MyBookings.share.close` | Close | වසන්න | மூடு | | |
| `MyBookings.share.name` | Their name | ඔවුන්ගේ නම | அவர்களின் பெயர் | | |
| `MyBookings.share.nameHint` | As you know them. Only you and the institution see it. | ඔබ ඔවුන් හඳුනන ලෙස. ඔබට සහ ආයතනයට පමණක් එය පෙනේ. | நீங்கள் அவர்களை அறிந்தபடி. நீங்களும் நிறுவனமும் மட்டுமே பார்க்க முடியும். | | |
| `MyBookings.share.nameRequired` | Please enter their name. | ඔවුන්ගේ නම ඇතුළත් කරන්න. | அவர்களின் பெயரை உள்ளிடவும். | | |
| `MyBookings.share.note` | A note (optional) | සටහනක් (අත්‍යවශ්‍ය නැත) | குறிப்பு (விருப்பம்) | | |
| `MyBookings.share.noteHint` | For example: brings the dessert. | උදාහරණයක්: අතුරුපස රැගෙන එයි. | உதாரணமாக: இனிப்பு கொண்டு வருவார். | | |
| `MyBookings.share.make` | Make a link | සබැඳියක් සාදන්න | இணைப்பை உருவாக்கு | | |
| `MyBookings.share.made` | A link for {name} | {name} සඳහා සබැඳියක් | {name} க்கான இணைப்பு | | |
| `MyBookings.share.linkLabel` | The link for {name} | {name} සඳහා සබැඳිය | {name} க்கான இணைப்பு | | |
| `MyBookings.share.onceOnly` | Copy it now: for safety we show each link only once. Send it by WhatsApp or a text. | දැන්ම පිටපත් කරන්න: ආරක්ෂාව සඳහා අපි සෑම සබැඳියක්ම එක් වරක් පමණක් පෙන්වමු. WhatsApp හෝ කෙටි පණිවිඩයකින් යවන්න. | இப்போதே நகலெடுக்கவும்: பாதுகாப்பிற்காக ஒவ்வொரு இணைப்பையும் ஒரு முறை மட்டுமே காட்டுவோம். WhatsApp அல்லது குறுஞ்செய்தி மூலம் அனுப்பவும். | | |
| `MyBookings.share.copy` | Copy link | සබැඳිය පිටපත් කරන්න | இணைப்பை நகலெடு | | |
| `MyBookings.share.copied` | Link copied. | සබැඳිය පිටපත් කළා. | இணைப்பு நகலெடுக்கப்பட்டது. | | |
| `MyBookings.share.copyFailed` | Could not copy. Select the link and copy it yourself. | පිටපත් කළ නොහැකි විය. සබැඳිය තෝරා ඔබම පිටපත් කරන්න. | நகலெடுக்க முடியவில்லை. இணைப்பைத் தேர்ந்தெடுத்து நீங்களே நகலெடுக்கவும். | | |
| `MyBookings.share.shareVia` | Share… | බෙදා ගන්න… | பகிர்… | | |
| `MyBookings.share.shareText` | Join me in giving this dana on Sethika | Sethika හි මෙම දානය දීමට මා සමඟ එක්වන්න | Sethika இல் இந்த தானத்தை வழங்க என்னுடன் சேருங்கள் | | |
| `MyBookings.share.another` | Invite someone else | තවත් අයෙකුට ආරාධනා කරන්න | வேறொருவரை அழை | | |
| `MyBookings.share.withdraw` | Withdraw | ඉල්ලා අස් කරන්න | திரும்பப் பெறு | | |
| `MyBookings.share.withdrawn` | {name}'s invitation is withdrawn. | {name} ගේ ආරාධනාව ඉල්ලා අස් කළා. | {name} இன் அழைப்பு திரும்பப் பெறப்பட்டது. | | |
| `MyBookings.share.status.Invited` | Invited | ආරාධිතයි | அழைக்கப்பட்டார் | | |
| `MyBookings.share.status.Joined` | Joined | එක්වුණා | சேர்ந்தார் | | |
| `MyBookings.share.status.Declined` | Declined | ප්‍රතික්ෂේප කළා | மறுத்தார் | | |
| `Calendar.booking.sharedWith` | Shared with: {names} | බෙදාගත්තේ: {names} | பகிர்ந்தவர்கள்: {names} | | |
| `Memorial.title` | Memorial dana planner | අනුස්මරණ දාන සැලසුම | நினைவு தான திட்டம் | | |
| `Memorial.intro` | Remember the people you have lost on the days their faith remembers them. We show each day, and remind you a week before. | ඔබට අහිමි වූවන් ඔවුන්ගේ ආගම ඔවුන් සිහිපත් කරන දිනවල සිහිපත් කරන්න. අපි සෑම දිනයක්ම පෙන්වා සතියකට පෙර මතක් කරමු. | நீங்கள் இழந்தவர்களை அவர்களின் சமயம் நினைவுகூரும் நாட்களில் நினைவுகூருங்கள். ஒவ்வொரு நாளையும் காட்டி, ஒரு வாரத்திற்கு முன் நினைவூட்டுவோம். | | |
| `Memorial.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயற்சிக்கவும் | | |
| `Memorial.draftNote` | The memorial days are drafts until religious leaders confirm them, so please check a date with your temple, kovil, mosque or church. | ආගමික නායකයන් තහවුරු කරන තුරු අනුස්මරණ දින කෙටුම්පත් පමණි; දිනයක් ඔබේ පන්සල, කෝවිල, පල්ලිය හෝ දේවස්ථානය සමඟ තහවුරු කර ගන්න. | சமயத் தலைவர்கள் உறுதிப்படுத்தும் வரை நினைவு நாட்கள் வரைவுகள் மட்டுமே; ஒரு தேதியை உங்கள் விகாரை, கோவில், பள்ளிவாசல் அல்லது தேவாலயத்துடன் உறுதிப்படுத்தவும். | | |
| `Memorial.add` | Remember someone | කෙනෙකු සිහිපත් කරන්න | ஒருவரை நினைவுகூர | | |
| `Memorial.none` | No one yet. Add someone to see their memorial days. | තවම කිසිවෙක් නැත. ඔවුන්ගේ අනුස්මරණ දින බැලීමට කෙනෙකු එක් කරන්න. | இன்னும் யாரும் இல்லை. நினைவு நாட்களைப் பார்க்க ஒருவரைச் சேர்க்கவும். | | |
| `Memorial.passed` | passed away {date} | අභාවය {date} | மறைவு {date} | | |
| `Memorial.noObservances` | Their faith has no memorial days listed yet. | ඔවුන්ගේ ආගමට තවම අනුස්මරණ දින ලැයිස්තුගත කර නැත. | அவர்களின் சமயத்திற்கு இன்னும் நினைவு நாட்கள் பட்டியலிடப்படவில்லை. | | |
| `Memorial.observances` | Memorial days | අනුස්මරණ දින | நினைவு நாட்கள் | | |
| `Memorial.archive` | Remove this plan | මෙම සැලසුම ඉවත් කරන්න | இந்தத் திட்டத்தை நீக்கு | | |
| `Memorial.yearNumber` | {name} (year {number}) | {name} (වසර {number}) | {name} (ஆண்டு {number}) | | |
| `Memorial.state.booked` | Booked | වෙන් කර ඇත | முன்பதிவு செய்யப்பட்டது | | |
| `Memorial.state.toBook` | Not booked yet | තවම වෙන් කර නැත | இன்னும் முன்பதிவு இல்லை | | |
| `Memorial.state.past` | Gone by | ගත වී ඇත | கடந்துவிட்டது | | |
| `Memorial.everyYear` | every year | සෑම වසරකම | ஒவ்வொரு ஆண்டும் | | |
| `Memorial.find` | Find a place | ස්ථානයක් සොයන්න | இடம் தேடு | | |
| `Memorial.findLabel` | Find a place for {name} on {date} | {date} දින {name} සඳහා ස්ථානයක් සොයන්න | {date} அன்று {name} க்கு இடம் தேடு | | |
| `Memorial.everyYearHint` | In the booking form you can choose to book it every year. | වෙන්කිරීමේ පෝරමයේදී එය සෑම වසරකම වෙන් කිරීමට තෝරාගත හැක. | முன்பதிவுப் படிவத்தில் இதை ஒவ்வொரு ஆண்டும் முன்பதிவு செய்யத் தேர்வு செய்யலாம். | | |
| `Memorial.form.title` | Remember someone | කෙනෙකු සිහිපත් කරන්න | ஒருவரை நினைவுகூர | | |
| `Memorial.form.description` | Only you see this plan. | මෙම සැලසුම ඔබට පමණක් පෙනේ. | இந்தத் திட்டத்தை நீங்கள் மட்டுமே பார்க்க முடியும். | | |
| `Memorial.form.name` | Their name | ඔවුන්ගේ නම | அவர்களின் பெயர் | | |
| `Memorial.form.nameRequired` | Please enter their name. | ඔවුන්ගේ නම ඇතුළත් කරන්න. | அவர்களின் பெயரை உள்ளிடவும். | | |
| `Memorial.form.relationship` | Who they were to you (optional) | ඔවුන් ඔබට කවුද (අත්‍යවශ්‍ය නැත) | அவர் உங்களுக்கு யார் (விருப்பம்) | | |
| `Memorial.form.relationshipNone` | Not given | සඳහන් කර නැත | குறிப்பிடவில்லை | | |
| `Memorial.form.invalid` | Please choose again. | නැවත තෝරන්න. | மீண்டும் தேர்வு செய்யவும். | | |
| `Memorial.form.dateOfPassing` | Date of passing | අභාවප්‍රාප්ත වූ දිනය | மறைந்த தேதி | | |
| `Memorial.form.dateRequired` | Please enter the date of passing. | අභාවප්‍රාප්ත වූ දිනය ඇතුළත් කරන්න. | மறைந்த தேதியை உள்ளிடவும். | | |
| `Memorial.form.religion` | Their faith | ඔවුන්ගේ ආගම | அவர்களின் சமயம் | | |
| `Memorial.form.religionHint` | The memorial days follow this faith's customs. | අනුස්මරණ දින මෙම ආගමේ සිරිත් අනුව වේ. | நினைவு நாட்கள் இந்த சமயத்தின் வழக்கங்களைப் பின்பற்றும். | | |
| `Memorial.form.religionRequired` | Please choose a faith. | ආගමක් තෝරන්න. | ஒரு சமயத்தைத் தேர்வு செய்யவும். | | |
| `Memorial.form.rulesTitle` | The memorial days we will show | අපි පෙන්වන අනුස්මරණ දින | நாங்கள் காட்டும் நினைவு நாட்கள் | | |
| `Memorial.form.notes` | Notes (optional) | සටහන් (අත්‍යවශ්‍ය නැත) | குறிப்புகள் (விருப்பம்) | | |
| `Memorial.form.notesHint` | Only for you. | ඔබට පමණි. | உங்களுக்கு மட்டும். | | |
| `Memorial.form.cancel` | Cancel | අවලංගු කරන්න | ரத்து | | |
| `Memorial.form.save` | Save | සුරකින්න | சேமி | | |
| `Memorial.form.created` | We will remember {name}'s days with you. | {name} ගේ දින අපි ඔබ සමඟ සිහිපත් කරමු. | {name} இன் நாட்களை உங்களுடன் நினைவுகூர்வோம். | | |
| `Memorial.archiveSheet.title` | Remove the plan for {name}? | {name} සඳහා වූ සැලසුම ඉවත් කරන්නද? | {name} க்கான திட்டத்தை நீக்கவா? | | |
| `Memorial.archiveSheet.description` | Its reminders stop. Bookings already made from it stay; you can cancel them in My bookings. | එහි මතක් කිරීම් නවතී. එයින් කළ වෙන්කිරීම් පවතී; මගේ වෙන්කිරීම් තුළ ඒවා අවලංගු කළ හැක. | அதன் நினைவூட்டல்கள் நிற்கும். அதிலிருந்து செய்த முன்பதிவுகள் இருக்கும்; என் முன்பதிவுகளில் ரத்து செய்யலாம். | | |
| `Memorial.archiveSheet.keep` | Keep it | තබා ගන්න | வைத்திரு | | |
| `Memorial.archiveSheet.confirm` | Remove | ඉවත් කරන්න | நீக்கு | | |
| `Share.title` | Share a dana | දානයක් බෙදා ගන්න | ஒரு தானத்தைப் பகிரவும் | | |
| `Share.notFound` | This link is not valid, or the invitation was withdrawn. | මෙම සබැඳිය වලංගු නැත, නැතහොත් ආරාධනාව ඉල්ලා අස්කර ඇත. | இந்த இணைப்பு செல்லாது, அல்லது அழைப்பு திரும்பப் பெறப்பட்டது. | | |
| `Share.invitedBy` | {name} invites you to give with them | {name} ඔවුන් සමඟ දන් දීමට ඔබට ආරාධනා කරයි | {name} அவர்களுடன் வழங்க உங்களை அழைக்கிறார் | | |
| `Share.where` | Where | කොහේද | எங்கே | | |
| `Share.when` | When | කවදාද | எப்போது | | |
| `Share.inMemoryOfLabel` | In memory of | සිහිවීම පිණිස | நினைவாக | | |
| `Share.joined` | You have joined this dana. | ඔබ මෙම දානයට එක්වුණා. | நீங்கள் இந்த தானத்தில் சேர்ந்துள்ளீர்கள். | | |
| `Share.declined` | You have declined. Thank you for letting them know. | ඔබ ප්‍රතික්ෂේප කළා. ඔවුන්ට දැනුම් දීම ගැන ස්තූතියි. | நீங்கள் மறுத்துவிட்டீர்கள். தெரிவித்ததற்கு நன்றி. | | |
| `Share.joinedNext` | It is under Shared with me in My bookings, and we will remind you the day before. | එය මගේ වෙන්කිරීම් හි මා සමඟ බෙදාගත් යටතේ ඇත; අපි පෙර දින මතක් කරමු. | இது என் முன்பதிவுகளில் என்னுடன் பகிரப்பட்டவை என்பதன் கீழ் உள்ளது; முந்தைய நாள் நினைவூட்டுவோம். | | |
| `Share.seeMine` | See my bookings | මගේ වෙන්කිරීම් බලන්න | என் முன்பதிவுகளைப் பார்க்கவும் | | |
| `Share.booking.Open` | This booking is open. | මෙම වෙන්කිරීම විවෘතයි. | இந்த முன்பதிவு திறந்துள்ளது. | | |
| `Share.booking.Cancelled` | This booking was cancelled. | මෙම වෙන්කිරීම අවලංගු කර ඇත. | இந்த முன்பதிவு ரத்து செய்யப்பட்டது. | | |
| `Share.booking.Rejected` | The institution could not accept this booking. | ආයතනයට මෙම වෙන්කිරීම භාර ගත නොහැකි විය. | நிறுவனத்தால் இந்த முன்பதிவை ஏற்க முடியவில்லை. | | |
| `Share.booking.Past` | This dana has already taken place. | මෙම දානය දැනටමත් පැවැත්විණි. | இந்த தானம் ஏற்கனவே நடந்துவிட்டது. | | |
| `Share.invitation.Invited` | This invitation is waiting for an answer. | මෙම ආරාධනාව පිළිතුරක් බලාපොරොත්තුවෙන් ඇත. | இந்த அழைப்பு பதிலுக்காகக் காத்திருக்கிறது. | | |
| `Share.invitation.Joined` | This invitation has already been accepted. | මෙම ආරාධනාව දැනටමත් භාරගෙන ඇත. | இந்த அழைப்பு ஏற்கனவே ஏற்கப்பட்டது. | | |
| `Share.invitation.Declined` | This invitation has already been declined. | මෙම ආරාධනාව දැනටමත් ප්‍රතික්ෂේප කර ඇත. | இந்த அழைப்பு ஏற்கனவே மறுக்கப்பட்டது. | | |
| `Share.signInFirst` | Sign in to answer. It takes a minute, and we will bring you straight back here. | පිළිතුරු දීමට පුරනය වන්න. එයට විනාඩියක් ගතවන අතර අපි ඔබව නැවත මෙහි ගෙන එන්නෙමු. | பதிலளிக்க உள்நுழையவும். ஒரு நிமிடம் ஆகும், உங்களை நேராக இங்கே திரும்பக் கொண்டு வருவோம். | | |
| `Share.signIn` | Sign in | පුරනය වන්න | உள்நுழை | | |
| `Share.createAccount` | Create an account | ගිණුමක් සාදන්න | கணக்கை உருவாக்கு | | |
| `Share.institutionSession` | You are signed in for an institution. Switch to your own account to answer. | ඔබ ආයතනයක් සඳහා පුරනය වී ඇත. පිළිතුරු දීමට ඔබේම ගිණුමට මාරු වන්න. | நீங்கள் ஒரு நிறுவனத்திற்காக உள்நுழைந்துள்ளீர்கள். பதிலளிக்க உங்கள் சொந்தக் கணக்கிற்கு மாறவும். | | |
| `Share.notAllowed` | This account cannot join bookings. | මෙම ගිණුමට වෙන්කිරීම්වලට එක්විය නොහැක. | இந்தக் கணக்கால் முன்பதிவுகளில் சேர முடியாது. | | |
| `Share.ask` | Will you give this dana with them? | ඔබ ඔවුන් සමඟ මෙම දානය දෙනවාද? | அவர்களுடன் இந்த தானத்தை வழங்குவீர்களா? | | |
| `Share.join` | Join | එක්වන්න | சேர் | | |
| `Share.decline` | Decline | ප්‍රතික්ෂේප කරන්න | மறு | | |

## Standing sponsor texts (2026-11-02, `STANDING_*`, `DAY_REQUEST_*`, plan 08 K.3)

Sent to an institution's regular donor when one of their days changes, and to a donor who asked for a held day
when the institution answers. `{institution}`, `{date}`, `{time}`, `{last}`, `{other}`, `{bookingNo}` and
`{requestNo}` are filled in by the app. Drafts, written by the developer; please check them, and keep each as
short as it can be (SMS segments).

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `STANDING_CREATED` | Sethika: {institution}, {date} {time}. You are now a regular donor here; this is your first day. The institution will tell you of any change. | Sethika: {institution}, {date} {time}. ඔබ දැන් මෙම ආයතනයේ නිත්‍ය දායකයෙකි; මෙය ඔබේ පළමු දිනයයි. වෙනසක් ඇත්නම් ආයතනය දන්වනු ඇත. | Sethika: {institution}, {date} {time}. நீங்கள் இப்போது இந்நிறுவனத்தின் வழக்கமான நன்கொடையாளர்; இது உங்கள் முதல் நாள். மாற்றம் இருந்தால் நிறுவனம் தெரிவிக்கும். | | |
| `STANDING_SKIPPED` | Sethika: {institution}, {date} {time}. As asked, you will not give on this day; it is open to others. Your other days stay. | Sethika: {institution}, {date} {time}. ඉල්ලූ පරිදි මෙදින ඔබ දානය නොදෙයි; එය අන් අයට විවෘතයි. ඔබේ අනෙක් දින එලෙසම පවතී. | Sethika: {institution}, {date} {time}. கேட்டபடி இந்நாளில் நீங்கள் வழங்கவில்லை; இது மற்றவர்களுக்குத் திறந்துள்ளது. உங்கள் மற்ற நாட்கள் அப்படியே. | | |
| `STANDING_RELEASED` | Sethika: {institution}, {date} {time}. The institution gave this day of yours to a donor who asked for it. Your other days stay. | Sethika: {institution}, {date} {time}. ඉල්ලීමක් කළ දායකයෙකුට ආයතනය ඔබේ මෙම දිනය ලබා දුන්නා. ඔබේ අනෙක් දින එලෙසම පවතී. | Sethika: {institution}, {date} {time}. கேட்ட ஒரு நன்கொடையாளருக்கு நிறுவனம் உங்கள் இந்நாளை வழங்கியது. உங்கள் மற்ற நாட்கள் அப்படியே. | | |
| `STANDING_BLOCKED` | Sethika: {institution}, {date} to {last}. The institution needs these days for another use, so your regular days in them are cancelled. | Sethika: {institution}, {date} සිට {last} දක්වා. ආයතනයට මෙම දින වෙනත් කටයුත්තකට අවශ්‍ය බැවින් ඒවායේ ඔබේ නිත්‍ය දින අවලංගුයි. අනෙක් දින එලෙසම පවතී. | Sethika: {institution}, {date} முதல் {last} வரை. இந்நாட்கள் நிறுவனத்துக்கு வேறு தேவைக்கு வேண்டும், எனவே அவற்றில் உங்கள் வழக்கமான நாட்கள் ரத்து. மற்ற நாட்கள் அப்படியே. | | |
| `STANDING_SWAPPED` | Sethika: {institution}, {date} {time}. Your day {other} was swapped with another regular donor's: this time, please give on {date} instead. | Sethika: {institution}, {date} {time}. ඔබේ {other} දිනය තවත් නිත්‍ය දායකයෙකුගේ දිනයක් සමඟ මාරු කළා: මෙවර කරුණාකර {date} දින දානය දෙන්න. | Sethika: {institution}, {date} {time}. உங்கள் {other} நாள் மற்றொரு வழக்கமான நன்கொடையாளரின் நாளுடன் மாற்றப்பட்டது: இம்முறை தயவுசெய்து {date} அன்று வழங்கவும். | | |
| `STANDING_SWAPPED_ON` | Sethika: {institution}, {date} {time}. Your regular days were swapped with another donor's: from {date} on, you give on their days. | Sethika: {institution}, {date} {time}. ඔබේ නිත්‍ය දින තවත් නිත්‍ය දායකයෙකුගේ දින සමඟ මාරු කළා: {date} සිට ඔබ දානය දෙන්නේ ඔවුන්ගේ දිනවලයි. | Sethika: {institution}, {date} {time}. உங்கள் வழக்கமான நாட்கள் மற்றொரு வழக்கமான நன்கொடையாளரின் நாட்களுடன் மாற்றப்பட்டன: {date} முதல் அவர்களின் நாட்களில் வழங்குவீர்கள். | | |
| `DAY_REQUEST_GIVEN` | Sethika {bookingNo}: {institution}, {date} {time}. The institution gave you the day you asked for; it is booked for you. | Sethika {bookingNo}: {institution}, {date} {time}. ඔබ ඉල්ලූ දිනය ආයතනය ඔබට ලබා දුන්නා; එය ඔබට වෙන් කර ඇත. | Sethika {bookingNo}: {institution}, {date} {time}. நீங்கள் கேட்ட நாளை நிறுவனம் உங்களுக்கு வழங்கியது; அது உங்களுக்கு முன்பதிவு செய்யப்பட்டது. | | |
| `DAY_REQUEST_DECLINED` | Sethika {requestNo}: {institution}, {date} {time}. The institution could not give you the day you asked for. Sign in to see why. | Sethika {requestNo}: {institution}, {date} {time}. ඔබ ඉල්ලූ දිනය ලබා දීමට ආයතනයට නොහැකි විය. හේතුව බැලීමට පිවිසෙන්න. | Sethika {requestNo}: {institution}, {date} {time}. நீங்கள் கேட்ட நாளை நிறுவனத்தால் வழங்க இயலவில்லை. காரணத்தைப் பார்க்க உள்நுழையவும். | | |

## Standing sponsor screens (2026-11-02, `Sponsors`, `Calendar`, `Discover`, `MyBookings`, `Booking.errors`, plan 08 K.4)

Setting up a regular donor's days (pattern picker, preview, result), an arrangement's days (skip, end, swap), the
calendar's regular donors and asked-for days, asking for a held day, and My bookings' requests. Drafts by Claude;
`{...}` are filled in by the app, and `{x, plural, ...}` keeps its structure.

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Sponsors.book.setUp` | Set up regular days | නිත්‍ය දින සකසන්න | வழக்கமான நாட்களை அமைக்கவும் | | |
| `Sponsors.book.setUpNamed` | Set up regular days for {name} | {name} සඳහා නිත්‍ය දින සකසන්න | {name} க்கு வழக்கமான நாட்களை அமைக்கவும் | | |
| `Sponsors.book.arrangementsOf` | {name}'s regular days | {name} ගේ නිත්‍ය දින | {name} இன் வழக்கமான நாட்கள் | | |
| `Sponsors.book.ended` | Ended | අවසන් | முடிந்தது | | |
| `Sponsors.pattern.Once` | Once, on {date} | එක් වරක්, {date} | ஒரு முறை, {date} | | |
| `Sponsors.pattern.Daily` | Every day | සෑම දිනකම | ஒவ்வொரு நாளும் | | |
| `Sponsors.pattern.Weekly` | Every {days} | සෑම {days} | ஒவ்வொரு {days} | | |
| `Sponsors.pattern.Monthly` | {interval, plural, =1 {Every month} =2 {Every other month} other {Every # months}}, on {count, plural, one {day} other {days}} {days} | {interval, plural, =1 {සෑම මසකම} =2 {මාස දෙකකට වරක්} other {මාස #කට වරක්}}, {count, plural, one {දිනය} other {දින}} {days} | {interval, plural, =1 {ஒவ்வொரு மாதமும்} =2 {இரண்டு மாதங்களுக்கு ஒருமுறை} other {# மாதங்களுக்கு ஒருமுறை}}, {count, plural, one {நாள்} other {நாட்கள்}} {days} | | |
| `Sponsors.pattern.Yearly` | Every year on {date} | සෑම වසරකම {date} | ஒவ்வொரு ஆண்டும் {date} | | |
| `Sponsors.pattern.from` | from {date} | {date} සිට | {date} முதல் | | |
| `Sponsors.pattern.until` | until {date} | {date} දක්වා | {date} வரை | | |
| `Sponsors.new.title` | Regular days for {name} | {name} ගේ නිත්‍ය දින | {name} இன் வழக்கமான நாட்கள் | | |
| `Sponsors.new.description` | Each day is booked for them and accepted at once. A day already taken or closed is listed, not booked. | සෑම දිනයක්ම ඔවුන් සඳහා වෙන් කර වහාම පිළිගැනේ. දැනටමත් ගත් හෝ වසා ඇති දිනයක් ලැයිස්තුගත කෙරේ, වෙන් නොකෙරේ. | ஒவ்வொரு நாளும் அவர்களுக்காக முன்பதிவு செய்யப்பட்டு உடனே ஏற்கப்படும். ஏற்கனவே எடுக்கப்பட்ட அல்லது மூடிய நாள் பட்டியலிடப்படும், முன்பதிவு செய்யப்படாது. | | |
| `Sponsors.new.noSlots` | Add a meal first, on the Meals screen. | පළමුව ආහාර වේල් තිරයෙන් ආහාර වේලක් එක් කරන්න. | முதலில் உணவுகள் திரையில் ஒரு உணவைச் சேர்க்கவும். | | |
| `Sponsors.new.meal` | Meal | ආහාර වේල | உணவு | | |
| `Sponsors.new.pattern` | How often | කොපමණ වාරයක් | எத்தனை முறை | | |
| `Sponsors.new.patterns.Once` | One day | එක් දිනක් | ஒரு நாள் | | |
| `Sponsors.new.patterns.Daily` | Every day | සෑම දිනකම | ஒவ்வொரு நாளும் | | |
| `Sponsors.new.patterns.Weekly` | Every week | සෑම සතියකම | ஒவ்வொரு வாரமும் | | |
| `Sponsors.new.patterns.Monthly` | Every month | සෑම මසකම | ஒவ்வொரு மாதமும் | | |
| `Sponsors.new.patterns.Yearly` | Every year | සෑම වසරකම | ஒவ்வொரு ஆண்டும் | | |
| `Sponsors.new.weekDays` | On these days of the week | සතියේ මෙම දිනවල | வாரத்தின் இந்த நாட்களில் | | |
| `Sponsors.new.monthDays` | On these days of the month | මාසයේ මෙම දිනවල | மாதத்தின் இந்த நாட்களில் | | |
| `Sponsors.new.monthDaysHint` | A day a month does not have (the 31st in April) is skipped that month. | මාසයක නැති දිනයක් (අප්‍රේල් 31) එම මාසයේ මඟ හැරේ. | ஒரு மாதத்தில் இல்லாத நாள் (ஏப்ரல் 31) அந்த மாதம் தவிர்க்கப்படும். | | |
| `Sponsors.new.interval` | Repeat | නැවත | மீண்டும் | | |
| `Sponsors.new.intervals` | {n, plural, =1 {Every month} =2 {Every other month} other {Every # months}} | {n, plural, =1 {සෑම මසකම} =2 {මාස දෙකකට වරක්} other {මාස #කට වරක්}} | {n, plural, =1 {ஒவ்வொரு மாதமும்} =2 {இரண்டு மாதங்களுக்கு ஒருமுறை} other {# மாதங்களுக்கு ஒருமுறை}} | | |
| `Sponsors.new.intervalHint` | Counted from the month it starts. | ආරම්භ වන මාසයේ සිට ගණන් කෙරේ. | தொடங்கும் மாதத்திலிருந்து கணக்கிடப்படும். | | |
| `Sponsors.new.yearDate` | On this day each year | සෑම වසරකම මෙම දිනයේ | ஒவ்வொரு ஆண்டும் இந்த நாளில் | | |
| `Sponsors.new.yearHint` | 29 February is given only in leap years. | පෙබරවාරි 29 ලබා දෙන්නේ අධික අවුරුදුවල පමණි. | பிப்ரவரி 29 நெட்டாண்டுகளில் மட்டுமே. | | |
| `Sponsors.new.yearMonth` | Month | මාසය | மாதம் | | |
| `Sponsors.new.yearDay` | Day | දිනය | நாள் | | |
| `Sponsors.new.onDate` | Day | දිනය | நாள் | | |
| `Sponsors.new.startDate` | First day | පළමු දිනය | முதல் நாள் | | |
| `Sponsors.new.endDate` | Last day (optional) | අවසන් දිනය (අවශ්‍ය නම්) | கடைசி நாள் (விருப்பம்) | | |
| `Sponsors.new.endHint` | Leave empty for no end. | අවසානයක් නැත්නම් හිස්ව තබන්න. | முடிவு இல்லையெனில் காலியாக விடவும். | | |
| `Sponsors.new.preview` | Next days | ඊළඟ දින | அடுத்த நாட்கள் | | |
| `Sponsors.new.previewHint` | As the calendar stands now. | දින දර්ශනය දැන් පවතින ආකාරයට. | நாட்காட்டி இப்போது உள்ளபடி. | | |
| `Sponsors.new.previewWaiting` | Choose the days to see them here. | මෙහි දැකීමට දින තෝරන්න. | இங்கே பார்க்க நாட்களைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.previewFailed` | Could not show the days. You can still save. | දින පෙන්විය නොහැකි විය. ඔබට තවමත් සුරැකිය හැක. | நாட்களைக் காட்ட முடியவில்லை. நீங்கள் இன்னும் சேமிக்கலாம். | | |
| `Sponsors.new.previewNone` | These choices give no days. | මෙම තේරීම් කිසිදු දිනයක් නොදෙයි. | இந்தத் தேர்வுகள் எந்த நாளையும் தரவில்லை. | | |
| `Sponsors.new.free` | Free | නිදහස් | காலியாக உள்ளது | | |
| `Sponsors.new.later` | Booked when it opens | විවෘත වූ විට වෙන් කෙරේ | திறக்கும்போது முன்பதிவு செய்யப்படும் | | |
| `Sponsors.new.headcount` | People to feed | ආහාර දිය යුතු පිරිස | உணவளிக்க வேண்டியவர்கள் | | |
| `Sponsors.new.headcountHint` | Between {min} and {max} for this meal. | මෙම වේල සඳහා {min} සිට {max} දක්වා. | இந்த உணவுக்கு {min} முதல் {max} வரை. | | |
| `Sponsors.new.fulfilment` | Who provides the meal | ආහාරය සපයන්නේ කවුද | உணவை யார் வழங்குகிறார் | | |
| `Sponsors.new.inMemoryOf` | In memory of (optional) | සිහිවීම පිණිස (අවශ්‍ය නම්) | நினைவாக (விருப்பம்) | | |
| `Sponsors.new.notes` | Notes (optional) | සටහන් (අවශ්‍ය නම්) | குறிப்புகள் (விருப்பம்) | | |
| `Sponsors.new.cancel` | Cancel | අවලංගු කරන්න | ரத்து செய் | | |
| `Sponsors.new.create` | Set up | සකසන්න | அமைக்கவும் | | |
| `Sponsors.new.errors.donor` | This donor is no longer in your book. | මෙම දායකයා තවදුරටත් ඔබේ පොතේ නැත. | இந்த நன்கொடையாளர் இனி உங்கள் பதிவேட்டில் இல்லை. | | |
| `Sponsors.new.errors.mealSlot` | Choose a meal. | ආහාර වේලක් තෝරන්න. | ஒரு உணவைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.pattern` | These choices give no days. | මෙම තේරීම් කිසිදු දිනයක් නොදෙයි. | இந்தத் தேர்வுகள் எந்த நாளையும் தரவில்லை. | | |
| `Sponsors.new.errors.weekDays` | Choose at least one day of the week. | සතියේ අවම වශයෙන් එක් දිනයක් තෝරන්න. | வாரத்தின் குறைந்தது ஒரு நாளைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.monthDays` | Choose at least one day of the month. | මාසයේ අවම වශයෙන් එක් දිනයක් තෝරන්න. | மாதத்தின் குறைந்தது ஒரு நாளைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.monthInterval` | Choose how often. | කොපමණ වාරයක්දැයි තෝරන්න. | எத்தனை முறை என்பதைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.yearMonth` | Choose a month. | මාසයක් තෝරන්න. | ஒரு மாதத்தைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.yearDay` | That month has no such day. | එම මාසයේ එවැනි දිනයක් නැත. | அந்த மாதத்தில் அப்படி ஒரு நாள் இல்லை. | | |
| `Sponsors.new.errors.startDate` | Choose today or a later day. | අද හෝ පසු දිනයක් තෝරන්න. | இன்று அல்லது பிந்தைய நாளைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.endDate` | The last day must be on or after the first. | අවසන් දිනය පළමු දිනයට සමාන හෝ පසු විය යුතුය. | கடைசி நாள் முதல் நாளுக்குச் சமமாகவோ பின்னரோ இருக்க வேண்டும். | | |
| `Sponsors.new.errors.headcount` | Enter a number of people this meal allows. | මෙම වේලට ඉඩ දෙන පිරිසක් ඇතුළත් කරන්න. | இந்த உணவு அனுமதிக்கும் எண்ணிக்கையை உள்ளிடவும். | | |
| `Sponsors.new.errors.fulfilment` | Choose who provides the meal. | ආහාරය සපයන්නේ කවුදැයි තෝරන්න. | உணவை யார் வழங்குகிறார் என்பதைத் தேர்ந்தெடுக்கவும். | | |
| `Sponsors.new.errors.inMemoryOf` | Up to 200 letters. | අකුරු 200ක් දක්වා. | 200 எழுத்துகள் வரை. | | |
| `Sponsors.new.errors.notes` | Up to 500 letters. | අකුරු 500ක් දක්වා. | 500 எழுத்துகள் வரை. | | |
| `Sponsors.new.done.booked` | {count, plural, =0 {No day booked yet} one {# day booked} other {# days booked}} | {count, plural, =0 {තවම දිනයක් වෙන් කර නැත} one {දින # ක් වෙන් කෙරිණි} other {දින # ක් වෙන් කෙරිණි}} | {count, plural, =0 {இன்னும் எந்த நாளும் முன்பதிவு செய்யப்படவில்லை} one {# நாள் முன்பதிவு செய்யப்பட்டது} other {# நாட்கள் முன்பதிவு செய்யப்பட்டன}} | | |
| `Sponsors.new.done.notBooked` | Not booked: | වෙන් නොකළ දින: | முன்பதிவு செய்யப்படாதவை: | | |
| `Sponsors.new.done.later` | {count, plural, one {# more day is being booked now.} other {# more days are being booked now.}} | {count, plural, one {තවත් දින #ක් දැන් වෙන් කෙරේ.} other {තවත් දින #ක් දැන් වෙන් කෙරේ.}} | {count, plural, one {மேலும் # நாள் இப்போது முன்பதிவு செய்யப்படுகிறது.} other {மேலும் # நாட்கள் இப்போது முன்பதிவு செய்யப்படுகின்றன.}} | | |
| `Sponsors.new.done.close` | Close | වසන්න | மூடு | | |
| `Sponsors.clash.slot_inactive` | Meal not offered | වේල ලබා නොදේ | உணவு வழங்கப்படுவதில்லை | | |
| `Sponsors.clash.institution_not_active` | Not taking bookings | වෙන්කිරීම් භාර නොගනී | முன்பதிவுகள் ஏற்கப்படுவதில்லை | | |
| `Sponsors.clash.not_on_this_day` | Meal not served that day | එදින වේල සපයන්නේ නැත | அந்த நாளில் உணவு இல்லை | | |
| `Sponsors.clash.out_of_season` | Out of season | කාලය නොවේ | பருவம் அல்ல | | |
| `Sponsors.clash.date_blocked` | Closed that day | එදින වසා ඇත | அந்த நாள் மூடப்பட்டுள்ளது | | |
| `Sponsors.clash.too_soon` | Too soon for this meal's notice | මෙම වේලේ දැනුම්දීමට ඉතා ළඟයි | இந்த உணவின் அறிவிப்புக்கு மிக அருகில் | | |
| `Sponsors.clash.too_far` | Too far ahead | ඉතා දුරයි | மிகத் தொலைவில் | | |
| `Sponsors.clash.slot_full` | Full | පිරී ඇත | நிரம்பியது | | |
| `Sponsors.clash.held` | Held by another regular donor | වෙනත් නිත්‍ය දායකයෙකුට හිමියි | வேறொரு வழக்கமான நன்கொடையாளருக்கு உரியது | | |
| `Sponsors.clash.alreadyHeld` | Already theirs that day | එදින දැනටමත් ඔවුන්ගේ | அந்த நாள் ஏற்கனவே அவருடையது | | |
| `Sponsors.clash.alreadyDone` | Already booked | දැනටමත් වෙන් කර ඇත | ஏற்கனவே முன்பதிவு செய்யப்பட்டது | | |
| `Sponsors.clash.ended` | Ended | අවසන් | முடிந்தது | | |
| `Sponsors.clash.busy` | Busy: tried again tonight | කාර්යබහුලයි: අද රාත්‍රියේ නැවත උත්සාහ කෙරේ | பரபரப்பு: இன்றிரவு மீண்டும் முயற்சிக்கப்படும் | | |
| `Sponsors.detail.loading` | Regular days | නිත්‍ය දින | வழக்கமான நாட்கள் | | |
| `Sponsors.detail.title` | {name}'s regular days | {name} ගේ නිත්‍ය දින | {name} இன் வழக்கமான நாட்கள் | | |
| `Sponsors.detail.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயற்சிக்கவும் | | |
| `Sponsors.detail.people` | {count, plural, one {# person} other {# people}} | {count, plural, one {# දෙනා} other {# දෙනා}} | {count, plural, one {# நபர்} other {# நபர்கள்}} | | |
| `Sponsors.detail.inMemoryOf` | In memory of {name} | {name} සිහිවීම පිණිස | {name} நினைவாக | | |
| `Sponsors.detail.endedOn` | Ended on {date} | {date} දින අවසන් විය | {date} அன்று முடிந்தது | | |
| `Sponsors.detail.nextDays` | Next days | ඊළඟ දින | அடுத்த நாட்கள் | | |
| `Sponsors.detail.noDays` | No more days. | තවත් දින නැත. | மேலும் நாட்கள் இல்லை. | | |
| `Sponsors.detail.state.booked` | Booked | වෙන් කර ඇත | முன்பதிவு செய்யப்பட்டது | | |
| `Sponsors.detail.state.cancelled` | Cancelled on the calendar | දින දර්ශනයෙන් අවලංගු කෙරිණි | நாட்காட்டியில் ரத்து செய்யப்பட்டது | | |
| `Sponsors.detail.state.open` | Not booked yet: tried again tonight | තවම වෙන් කර නැත: අද රාත්‍රියේ නැවත උත්සාහ කෙරේ | இன்னும் முன்பதிவு இல்லை: இன்றிரவு மீண்டும் முயற்சிக்கப்படும் | | |
| `Sponsors.detail.state.upcoming` | Booked when it opens | විවෘත වූ විට වෙන් කෙරේ | திறக்கும்போது முன்பதிவு செய்யப்படும் | | |
| `Sponsors.detail.state.Skipped` | Cannot give | දිය නොහැක | வழங்க இயலாது | | |
| `Sponsors.detail.state.Released` | Given to someone who asked | ඉල්ලූ කෙනෙකුට දෙන ලදී | கேட்டவருக்கு வழங்கப்பட்டது | | |
| `Sponsors.detail.state.Blocked` | Closed by you | ඔබ විසින් වසා ඇත | நீங்கள் மூடியது | | |
| `Sponsors.detail.state.MovedTo` | Swapped away | මාරු කර ගියේය | மாற்றி அனுப்பப்பட்டது | | |
| `Sponsors.detail.state.MovedFrom` | Swapped in | මාරුවෙන් ලැබුණි | மாற்றிப் பெறப்பட்டது | | |
| `Sponsors.detail.skip` | Cannot give | දිය නොහැක | வழங்க இயலாது | | |
| `Sponsors.detail.skipNamed` | {date}: cannot give | {date}: දිය නොහැක | {date}: வழங்க இயலாது | | |
| `Sponsors.detail.skipTitle` | Cannot give on {date}? | {date} දිය නොහැකිද? | {date} அன்று வழங்க இயலாதா? | | |
| `Sponsors.detail.skipExplain` | The day opens for others and they are told, if they get texts. | දිනය අන් අයට විවෘත වන අතර, කෙටි පණිවිඩ ලැබෙන්නේ නම් ඔවුන්ට දන්වනු ලැබේ. | அந்த நாள் மற்றவர்களுக்குத் திறக்கும்; குறுஞ்செய்திகள் பெறுபவரானால் அவருக்குத் தெரிவிக்கப்படும். | | |
| `Sponsors.detail.reason` | Reason (optional) | හේතුව (අවශ්‍ය නම්) | காரணம் (விருப்பம்) | | |
| `Sponsors.detail.reasonHint` | Your staff see it. | ඔබේ කාර්ය මණ්ඩලයට පෙනේ. | உங்கள் ஊழியர்களுக்குத் தெரியும். | | |
| `Sponsors.detail.skipConfirm` | Let the day go | දිනය නිදහස් කරන්න | நாளை விடுவிக்கவும் | | |
| `Sponsors.detail.skipped` | {date} is let go. | {date} නිදහස් කෙරිණි. | {date} விடுவிக்கப்பட்டது. | | |
| `Sponsors.detail.end` | End | අවසන් කරන්න | முடிக்கவும் | | |
| `Sponsors.detail.endTitle` | End these regular days? | මෙම නිත්‍ය දින අවසන් කරන්නද? | இந்த வழக்கமான நாட்களை முடிக்கவா? | | |
| `Sponsors.detail.endExplain` | Every booked day after today is let go. Today's meal stays. | අදින් පසු වෙන් කළ සියලු දින නිදහස් කෙරේ. අද දානය ඉතිරි වේ. | இன்றைக்குப் பிறகு முன்பதிவு செய்த ஒவ்வொரு நாளும் விடுவிக்கப்படும். இன்றைய உணவு அப்படியே இருக்கும். | | |
| `Sponsors.detail.endReasonHint` | Shown on the cancelled days. | අවලංගු කළ දිනවල පෙන්වයි. | ரத்து செய்யப்பட்ட நாட்களில் காட்டப்படும். | | |
| `Sponsors.detail.endConfirm` | End | අවසන් කරන්න | முடிக்கவும் | | |
| `Sponsors.detail.ended` | These regular days have ended. | මෙම නිත්‍ය දින අවසන් විය. | இந்த வழக்கமான நாட்கள் முடிந்தன. | | |
| `Sponsors.detail.swap` | Swap a day | දිනයක් මාරු කරන්න | ஒரு நாளை மாற்றவும் | | |
| `Sponsors.detail.back` | Back | ආපසු | பின்செல் | | |
| `Sponsors.detail.close` | Close | වසන්න | மூடு | | |
| `Sponsors.swap.title` | Swap a day | දිනයක් මාරු කරන්න | ஒரு நாளை மாற்றவும் | | |
| `Sponsors.swap.description` | Two regular donors of the same meal give on each other's day. Both are told. | එකම වේලේ නිත්‍ය දායකයන් දෙදෙනෙක් එකිනෙකාගේ දිනයේ දන් දෙති. දෙදෙනාටම දන්වනු ලැබේ. | ஒரே உணவின் இரண்டு வழக்கமான நன்கொடையாளர்கள் ஒருவர் மற்றவரின் நாளில் வழங்குவர். இருவருக்கும் தெரிவிக்கப்படும். | | |
| `Sponsors.swap.noPartner` | No other regular donor gives this meal. | මෙම වේල දෙන වෙනත් නිත්‍ය දායකයෙක් නැත. | இந்த உணவை வழங்கும் வேறு வழக்கமான நன்கொடையாளர் இல்லை. | | |
| `Sponsors.swap.when` | For how long | කොපමණ කලකට | எவ்வளவு காலம் | | |
| `Sponsors.swap.once` | This time only | මෙවර පමණක් | இம்முறை மட்டும் | | |
| `Sponsors.swap.onceHint` | Both days must be booked. | දින දෙකම වෙන් කර තිබිය යුතුය. | இரண்டு நாட்களும் முன்பதிவு செய்யப்பட்டிருக்க வேண்டும். | | |
| `Sponsors.swap.permanent` | From now on | මින් ඉදිරියට | இனிமேல் | | |
| `Sponsors.swap.permanentHint` | From the chosen days, each gives on the other's days. | තෝරාගත් දින සිට, එක් එක් අය අනෙකාගේ දිනවල දෙති. | தேர்ந்தெடுத்த நாட்களிலிருந்து, ஒவ்வொருவரும் மற்றவரின் நாட்களில் வழங்குவர். | | |
| `Sponsors.swap.own` | {name}'s day | {name} ගේ දිනය | {name} இன் நாள் | | |
| `Sponsors.swap.partner` | With | සමඟ | இவருடன் | | |
| `Sponsors.swap.partnerDay` | Their day | ඔවුන්ගේ දිනය | அவரது நாள் | | |
| `Sponsors.swap.choose` | Choose a day | දිනයක් තෝරන්න | ஒரு நாளைத் தேர்ந்தெடுக்கவும் | | |
| `Sponsors.swap.confirm` | Swap | මාරු කරන්න | மாற்றவும் | | |
| `Sponsors.swap.done` | Swapped. | මාරු කෙරිණි. | மாற்றப்பட்டது. | | |
| `Sponsors.swap.back` | Back | ආපසු | பின்செல் | | |
| `Sponsors.errors.sponsorship_not_found` | These regular days are no longer here. | මෙම නිත්‍ය දින තවදුරටත් මෙහි නැත. | இந்த வழக்கமான நாட்கள் இனி இங்கு இல்லை. | | |
| `Sponsors.errors.sponsorship_ended` | These regular days have ended. | මෙම නිත්‍ය දින අවසන් වී ඇත. | இந்த வழக்கமான நாட்கள் முடிந்துவிட்டன. | | |
| `Sponsors.errors.day_already_changed` | That day has already been let go or swapped. | එම දිනය දැනටමත් නිදහස් කර හෝ මාරු කර ඇත. | அந்த நாள் ஏற்கனவே விடுவிக்கப்பட்டது அல்லது மாற்றப்பட்டது. | | |
| `Sponsors.errors.day_not_booked` | Both days must be booked to swap this time only. | මෙවර පමණක් මාරු කිරීමට දින දෙකම වෙන් කර තිබිය යුතුය. | இம்முறை மட்டும் மாற்ற இரண்டு நாட்களும் முன்பதிவு செய்யப்பட்டிருக்க வேண்டும். | | |
| `Calendar.standingDot` | A regular donor's day | නිත්‍ය දායකයෙකුගේ දිනය | வழக்கமான நன்கொடையாளரின் நாள் | | |
| `Calendar.standingLabel` | {count, plural, one {# regular donor.} other {# regular donors.}} | {count, plural, one {නිත්‍ය දායකයන් #.} other {නිත්‍ය දායකයන් #.}} | {count, plural, one {# வழக்கமான நன்கொடையாளர்.} other {# வழக்கமான நன்கொடையாளர்கள்.}} | | |
| `Calendar.booking.standing` | Regular donor | නිත්‍ය දායක | வழக்கமான நன்கொடையாளர் | | |
| `Calendar.dayRequests.heading` | Asked for a regular donor's day | නිත්‍ය දායකයෙකුගේ දිනයක් ඉල්ලා ඇත | வழக்கமான நன்கொடையாளரின் நாள் கேட்கப்பட்டுள்ளது | | |
| `Calendar.dayRequests.intro` | Giving the day lets the regular donor's day go and books the person who asked. | දිනය දීමෙන් නිත්‍ය දායකයාගේ දිනය නිදහස් වී ඉල්ලූ තැනැත්තාට වෙන් කෙරේ. | நாளை வழங்கினால் வழக்கமான நன்கொடையாளரின் நாள் விடுவிக்கப்பட்டு கேட்டவருக்கு முன்பதிவு செய்யப்படும். | | |
| `Calendar.dayRequests.asks` | {name} asks for this day | {name} මෙම දිනය ඉල්ලයි | {name} இந்த நாளைக் கேட்கிறார் | | |
| `Calendar.dayRequests.heldBy` | Held by {name} | {name} ට හිමියි | {name} க்கு உரியது | | |
| `Calendar.dayRequests.whose` | Whose day to give | කාගේ දිනය දිය යුතුද | யாருடைய நாளை வழங்குவது | | |
| `Calendar.dayRequests.give` | Give them the day | ඔවුන්ට දිනය දෙන්න | அவருக்கு நாளை வழங்கவும் | | |
| `Calendar.dayRequests.decline` | Keep it | තබා ගන්න | வைத்திருக்கவும் | | |
| `Calendar.dayRequests.declineReason` | Why? They read this when they sign in. | ඇයි? ඔවුන් පුරනය වූ විට මෙය කියවයි. | ஏன்? அவர் உள்நுழையும்போது இதைப் படிப்பார். | | |
| `Calendar.dayRequests.declineHint` | Optional. It is not texted. | අවශ්‍ය නම්. කෙටි පණිවිඩයෙන් නොයවයි. | விருப்பம். குறுஞ்செய்தியில் அனுப்பப்படாது. | | |
| `Calendar.dayRequests.confirmDecline` | Keep it with the regular donor | නිත්‍ය දායකයාට තබා ගන්න | வழக்கமான நன்கொடையாளரிடம் வைத்திருக்கவும் | | |
| `Calendar.dayRequests.back` | Back | ආපසු | பின்செல் | | |
| `Calendar.dayRequests.given` | {requestNo}: the day is {name}'s now. Both have been told. | {requestNo}: දිනය දැන් {name} ට. දෙදෙනාටම දන්වා ඇත. | {requestNo}: நாள் இப்போது {name} உடையது. இருவருக்கும் தெரிவிக்கப்பட்டது. | | |
| `Calendar.dayRequests.declined` | {requestNo}: kept with the regular donor. {name} has been told. | {requestNo}: නිත්‍ය දායකයාට තබා ගැනිණි. {name} ට දන්වා ඇත. | {requestNo}: வழக்கமான நன்கொடையாளரிடம் வைக்கப்பட்டது. {name} க்குத் தெரிவிக்கப்பட்டது. | | |
| `Discover.reason.held` | Held by a regular donor | නිත්‍ය දායකයෙකුට හිමියි | வழக்கமான நன்கொடையாளருக்கு உரியது | | |
| `Discover.heldAsk` | Held by a regular donor. You can ask for it. | නිත්‍ය දායකයෙකුට හිමියි. ඔබට එය ඉල්ලිය හැක. | வழக்கமான நன்கொடையாளருக்கு உரியது. நீங்கள் கேட்கலாம். | | |
| `Discover.legend.held` | Held by a regular donor: ask | නිත්‍ය දායකයෙකුට හිමියි: ඉල්ලන්න | வழக்கமான நன்கொடையாளருக்கு உரியது: கேட்கவும் | | |
| `Discover.ask.title` | Ask for {date} | {date} ඉල්ලන්න | {date} ஐக் கேட்கவும் | | |
| `Discover.ask.explain` | A regular donor gives on this day. The institution decides whether to give it to you. Asking holds nothing. | මෙදින නිත්‍ය දායකයෙක් දන් දෙයි. ඔබට එය දෙනවාද යන්න ආයතනය තීරණය කරයි. ඉල්ලීමෙන් කිසිවක් රඳවා නොගනී. | இந்த நாளில் ஒரு வழக்கமான நன்கொடையாளர் வழங்குகிறார். அதை உங்களுக்கு வழங்குவதா என்பதை நிறுவனம் முடிவு செய்யும். கேட்பது எதையும் ஒதுக்காது. | | |
| `Discover.ask.signInFirst` | Sign in to ask for this day. You will come back here. | මෙම දිනය ඉල්ලීමට පුරනය වන්න. ඔබ නැවත මෙහි පැමිණේ. | இந்த நாளைக் கேட்க உள்நுழையவும். நீங்கள் மீண்டும் இங்கு வருவீர்கள். | | |
| `Discover.ask.institutionSession` | You are signed in for an institution. Switch to your own account to ask. | ඔබ ආයතනයක් සඳහා පුරනය වී ඇත. ඉල්ලීමට ඔබේම ගිණුමට මාරු වන්න. | நீங்கள் ஒரு நிறுவனத்துக்காக உள்நுழைந்துள்ளீர்கள். கேட்க உங்கள் சொந்தக் கணக்குக்கு மாறவும். | | |
| `Discover.ask.notAllowed` | This account cannot ask for days. | මෙම ගිණුමට දින ඉල්ලිය නොහැක. | இந்தக் கணக்கால் நாட்களைக் கேட்க முடியாது. | | |
| `Discover.ask.headcount` | People you would feed | ඔබ ආහාර දෙන පිරිස | நீங்கள் உணவளிப்பவர்கள் | | |
| `Discover.ask.headcountHint` | Between {min} and {max}. | {min} සිට {max} දක්වා. | {min} முதல் {max} வரை. | | |
| `Discover.ask.headcountRange` | Enter a number between {min} and {max}. | {min} සිට {max} දක්වා අංකයක් ඇතුළත් කරන්න. | {min} முதல் {max} வரையான எண்ணை உள்ளிடவும். | | |
| `Discover.ask.fulfilment` | Who provides the meal | ආහාරය සපයන්නේ කවුද | உணவை யார் வழங்குகிறார் | | |
| `Discover.ask.note` | A note for the institution (optional) | ආයතනයට සටහනක් (අවශ්‍ය නම්) | நிறுவனத்துக்கு ஒரு குறிப்பு (விருப்பம்) | | |
| `Discover.ask.noteHint` | For example, why this day matters to you. | උදාහරණයක් ලෙස, මෙම දිනය ඔබට වැදගත් වන්නේ ඇයි. | உதாரணமாக, இந்த நாள் உங்களுக்கு ஏன் முக்கியம். | | |
| `Discover.ask.cancel` | Cancel | අවලංගු කරන්න | ரத்து செய் | | |
| `Discover.ask.send` | Ask for this day | මෙම දිනය ඉල්ලන්න | இந்த நாளைக் கேட்கவும் | | |
| `Discover.ask.done` | Asked: {requestNo} | ඉල්ලා ඇත: {requestNo} | கேட்கப்பட்டது: {requestNo} | | |
| `Discover.ask.doneNext` | We will text you their answer. Your requests are in My bookings. | ඔවුන්ගේ පිළිතුර අපි ඔබට කෙටි පණිවිඩයෙන් දන්වමු. ඔබේ ඉල්ලීම් මගේ වෙන්කිරීම්වල ඇත. | அவர்களின் பதிலைக் குறுஞ்செய்தியில் தெரிவிப்போம். உங்கள் கோரிக்கைகள் எனது முன்பதிவுகளில் உள்ளன. | | |
| `Discover.ask.myBookings` | My bookings | මගේ වෙන්කිරීම් | எனது முன்பதிவுகள் | | |
| `Discover.ask.close` | Close | වසන්න | மூடு | | |
| `MyBookings.regular` | A regular day the institution set up for you | ආයතනය ඔබ සඳහා සැකසූ නිත්‍ය දිනයක් | நிறுவனம் உங்களுக்காக அமைத்த வழக்கமான நாள் | | |
| `MyBookings.requests.title` | Days I asked for | මා ඉල්ලූ දින | நான் கேட்ட நாட்கள் | | |
| `MyBookings.requests.status.Open` | Waiting for their answer | ඔවුන්ගේ පිළිතුරට රැඳී ඇත | அவர்களின் பதிலுக்குக் காத்திருக்கிறது | | |
| `MyBookings.requests.status.Given` | Given to you | ඔබට දෙන ලදී | உங்களுக்கு வழங்கப்பட்டது | | |
| `MyBookings.requests.status.Declined` | Kept with the regular donor | නිත්‍ය දායකයාට තබා ගැනිණි | வழக்கமான நன்கொடையாளரிடம் வைக்கப்பட்டது | | |
| `MyBookings.requests.status.Withdrawn` | Withdrawn | ඉවත් කර ගැනිණි | திரும்பப் பெறப்பட்டது | | |
| `MyBookings.requests.booking` | Booked as {bookingNo} | {bookingNo} ලෙස වෙන් කෙරිණි | {bookingNo} ஆக முன்பதிவு செய்யப்பட்டது | | |
| `MyBookings.requests.declinedBecause` | Their reason: {reason} | ඔවුන්ගේ හේතුව: {reason} | அவர்களின் காரணம்: {reason} | | |
| `Booking.errors.day_not_held` | This day is no longer held by a regular donor. The calendar has been refreshed. | මෙම දිනය තවදුරටත් නිත්‍ය දායකයෙකුට හිමි නැත. දින දර්ශනය යාවත්කාලීන කෙරිණි. | இந்த நாள் இனி வழக்கமான நன்கொடையாளருக்கு உரியதல்ல. நாட்காட்டி புதுப்பிக்கப்பட்டது. | | |
| `Booking.errors.day_request_open` | You have already asked for this day. We will text you their answer. | ඔබ දැනටමත් මෙම දිනය ඉල්ලා ඇත. ඔවුන්ගේ පිළිතුර අපි කෙටි පණිවිඩයෙන් දන්වමු. | நீங்கள் ஏற்கனவே இந்த நாளைக் கேட்டுள்ளீர்கள். அவர்களின் பதிலைக் குறுஞ்செய்தியில் தெரிவிப்போம். | | |
| `Booking.errors.day_request_answered` | This request has already been answered. | මෙම ඉල්ලීමට දැනටමත් පිළිතුරු දී ඇත. | இந்தக் கோரிக்கைக்கு ஏற்கனவே பதிலளிக்கப்பட்டது. | | |
| `Booking.errors.day_request_not_found` | This request could not be found. | මෙම ඉල්ලීම සොයාගත නොහැකි විය. | இந்தக் கோரிக்கையைக் கண்டுபிடிக்க முடியவில்லை. | | |

## Standing sponsors' own days (2026-11-04, `MyBookings`, `Booking.errors`, plan 08 K.5)

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `MyBookings.cantGive` | I can't give that day | මට එදින දිය නොහැක | அந்த நாளில் என்னால் வழங்க முடியாது | | |
| `MyBookings.skipSheet.title` | Can't give on this day? | මෙදින දන් දිය නොහැකිද? | இந்த நாளில் வழங்க முடியாதா? | | |
| `MyBookings.skipSheet.description` | {bookingNo}. Only this day is let go: your other regular days stay yours. The institution sees it, and the day opens for someone else. | {bookingNo}. මෙම දිනය පමණක් අත්හරිනු ලැබේ: ඔබේ අනෙක් නිත්‍ය දින ඔබටම රැඳේ. ආයතනයට එය පෙනෙන අතර, දිනය වෙනත් අයෙකුට විවෘත වේ. | {bookingNo}. இந்த நாள் மட்டுமே விடப்படும்: உங்கள் மற்ற வழக்கமான நாட்கள் உங்களிடமே இருக்கும். நிறுவனம் இதைப் பார்க்கும், இந்த நாள் வேறொருவருக்குக் கிடைக்கும். | | |
| `MyBookings.skipSheet.reason` | Reason (optional) | හේතුව (අත්‍යවශ්‍ය නොවේ) | காரணம் (விருப்பத்தேர்வு) | | |
| `MyBookings.skipSheet.reasonHint` | The institution will see it. | ආයතනයට එය පෙනෙනු ඇත. | நிறுவனம் இதைப் பார்க்கும். | | |
| `MyBookings.skipSheet.keep` | Keep this day | මෙම දිනය තබා ගන්න | இந்த நாளை வைத்திருங்கள் | | |
| `MyBookings.skipSheet.confirm` | I can't give that day | මට එදින දිය නොහැක | அந்த நாளில் என்னால் வழங்க முடியாது | | |
| `MyBookings.skipSheet.done` | {bookingNo} is let go. We will text you to confirm. | {bookingNo} අත්හරින ලදී. තහවුරු කිරීමට අපි ඔබට කෙටි පණිවිඩයක් එවන්නෙමු. | {bookingNo} விடப்பட்டது. உறுதிப்படுத்த உங்களுக்குக் குறுஞ்செய்தி அனுப்புவோம். | | |
| `Booking.errors.sponsorship_ended` | These regular days have ended. | මෙම නිත්‍ය දින අවසන් වී ඇත. | இந்த வழக்கமான நாட்கள் முடிந்துவிட்டன. | | |
| `Booking.errors.day_already_changed` | That day has already been let go or swapped. | එම දිනය දැනටමත් නිදහස් කර හෝ මාරු කර ඇත. | அந்த நாள் ஏற்கனவே விடுவிக்கப்பட்டது அல்லது மாற்றப்பட்டது. | | |

## Giving: donations and receipts (2026-11-04, `Giving`, `Shell`, `Admin`, plan 09 D.1)

| Key | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `Giving.money` | LKR {amount} | රු. {amount} | ரூ. {amount} | | |
| `Giving.itemsCounted` | {items} ({count}) | {items} ({count}) | {items} ({count}) | | |
| `Giving.methods.CASH` | Cash | මුදල් | ரொக்கம் | | |
| `Giving.methods.BANK_TRANSFER` | Bank transfer | බැංකු මාරුව | வங்கிப் பரிமாற்றம் | | |
| `Giving.methods.CHEQUE` | Cheque | චෙක්පත | காசோலை | | |
| `Giving.methods.CARD` | Card | කාඩ්පත | அட்டை | | |
| `Giving.methods.PAYHERE` | Online (PayHere) | මාර්ගගත (PayHere) | இணையவழி (PayHere) | | |
| `Giving.errors.invalid_details` | Some details need fixing. Check the fields marked below. | සමහර විස්තර නිවැරදි කළ යුතුයි. පහත සලකුණු කළ ක්ෂේත්‍ර බලන්න. | சில விவரங்களைத் திருத்த வேண்டும். கீழே குறிக்கப்பட்ட புலங்களைப் பாருங்கள். | | |
| `Giving.errors.donation_not_found` | There is no such receipt here. It may belong to another institution, or the link is wrong. | මෙහි එවැනි රිසිට්පතක් නැත. එය වෙනත් ආයතනයකට අයත් විය හැක, නැතහොත් සබැඳිය වැරදියි. | இங்கே அப்படி ஒரு பற்றுச்சீட்டு இல்லை. அது வேறு நிறுவனத்தினுடையதாக இருக்கலாம், அல்லது இணைப்பு தவறானது. | | |
| `Giving.errors.donor_not_found` | That person is not in your donor book any more. Choose them again, or record the gift under a typed name. | එම පුද්ගලයා තවදුරටත් ඔබේ දායක පොතේ නැත. ඔවුන් නැවත තෝරන්න, නැතහොත් නමක් ටයිප් කර පරිත්‍යාගය සටහන් කරන්න. | அந்த நபர் இனி உங்கள் நன்கொடையாளர் புத்தகத்தில் இல்லை. மீண்டும் தேர்ந்தெடுக்கவும், அல்லது தட்டச்சு செய்த பெயரில் நன்கொடையைப் பதிவு செய்யவும். | | |
| `Giving.errors.stale` | Someone changed this gift while you were looking at it. Look at the list again. | ඔබ බලා සිටියදී කෙනෙකු මෙම පරිත්‍යාගය වෙනස් කළා. ලැයිස්තුව නැවත බලන්න. | நீங்கள் பார்த்துக்கொண்டிருக்கும்போது யாரோ இந்த நன்கொடையை மாற்றினர். பட்டியலை மீண்டும் பாருங்கள். | | |
| `Giving.errors.already_void` | This gift is voided already. | මෙම පරිත්‍යාගය දැනටමත් අවලංගු කර ඇත. | இந்த நன்கொடை ஏற்கனவே ரத்து செய்யப்பட்டது. | | |
| `Giving.errors.void_own_record` | You recorded this gift, so someone else must void it. That second pair of eyes is what keeps receipts honest. | මෙම පරිත්‍යාගය සටහන් කළේ ඔබයි, එබැවින් එය අවලංගු කළ යුත්තේ වෙනත් අයෙකි. රිසිට්පත් අවංකව තබන්නේ එම දෙවන ඇස් යුගලයයි. | இந்த நன்கொடையைப் பதிவு செய்தது நீங்கள், எனவே வேறொருவர் அதை ரத்து செய்ய வேண்டும். பற்றுச்சீட்டுகளை நேர்மையாக வைப்பது அந்த இரண்டாவது பார்வையே. | | |
| `Giving.errors.invalid_month` | That month could not be read. | එම මාසය කියවිය නොහැකි විය. | அந்த மாதத்தைப் படிக்க முடியவில்லை. | | |
| `Giving.donations.title` | Donations and receipts | පරිත්‍යාග සහ රිසිට්පත් | நன்கொடைகளும் பற்றுச்சீட்டுகளும் | | |
| `Giving.donations.intro` | Every gift your institution receives, money or items, each with its own receipt number. A gift is never changed or deleted: a mistake is voided by a second person and recorded again. | ඔබේ ආයතනයට ලැබෙන සෑම පරිත්‍යාගයක්ම, මුදල් හෝ ද්‍රව්‍ය, එකිනෙකට වෙනම රිසිට් අංකයක් සමඟ. පරිත්‍යාගයක් කිසිදා වෙනස් කිරීම හෝ මැකීම නොකෙරේ: වැරැද්දක් දෙවන අයෙකු විසින් අවලංගු කර නැවත සටහන් කෙරේ. | உங்கள் நிறுவனம் பெறும் ஒவ்வொரு நன்கொடையும், பணமோ பொருளோ, தனித்தனி பற்றுச்சீட்டு எண்ணுடன். நன்கொடை ஒருபோதும் மாற்றப்படுவதோ நீக்கப்படுவதோ இல்லை: தவறு இரண்டாவது நபரால் ரத்து செய்யப்பட்டு மீண்டும் பதிவு செய்யப்படும். | | |
| `Giving.donations.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயற்சிக்கவும் | | |
| `Giving.donations.month` | Month | මාසය | மாதம் | | |
| `Giving.donations.previousMonth` | Previous month | පෙර මාසය | முந்தைய மாதம் | | |
| `Giving.donations.nextMonth` | Next month | ඊළඟ මාසය | அடுத்த மாதம் | | |
| `Giving.donations.record` | Record a gift | පරිත්‍යාගයක් සටහන් කරන්න | நன்கொடையைப் பதிவு செய் | | |
| `Giving.donations.totals` | {gifts, plural, =0 {No gifts kept} one {# gift} other {# gifts}}: {money} in money, {items, plural, =0 {none in items} one {# of items} other {# of items}}. | පරිත්‍යාග {gifts}: මුදලින් {money}, ද්‍රව්‍ය {items}. | நன்கொடைகள் {gifts}: பணமாக {money}, பொருட்களாக {items}. | | |
| `Giving.donations.voidedCount` | {count, plural, one {# voided, not counted} other {# voided, not counted}} | අවලංගු {count}, ගණන් නොකෙරේ | ரத்து {count}, கணக்கில் இல்லை | | |
| `Giving.donations.none` | No gifts recorded in this month. | මෙම මාසයේ පරිත්‍යාග සටහන් කර නැත. | இந்த மாதம் நன்கொடைகள் பதிவு செய்யப்படவில்லை. | | |
| `Giving.donations.methodLabel` | Paid by | ගෙවූ ආකාරය | செலுத்திய முறை | | |
| `Giving.donations.allMethods` | All | සියල්ල | அனைத்தும் | | |
| `Giving.donations.voidedLabel` | Show | පෙන්වන්න | காட்டு | | |
| `Giving.donations.voidedAll` | All gifts | සියලු පරිත්‍යාග | அனைத்து நன்கொடைகளும் | | |
| `Giving.donations.voidedKept` | Not voided | අවලංගු නොකළ | ரத்து செய்யாதவை | | |
| `Giving.donations.voidedOnly` | Voided | අවලංගු කළ | ரத்து செய்தவை | | |
| `Giving.donations.noMatch` | No gift this month matches what you chose. | ඔබ තේරූ දෙයට ගැළපෙන පරිත්‍යාගයක් මෙම මාසයේ නැත. | நீங்கள் தேர்ந்தெடுத்ததற்குப் பொருந்தும் நன்கொடை இந்த மாதம் இல்லை. | | |
| `Giving.donations.list` | Gifts this month | මෙම මාසයේ පරිත්‍යාග | இந்த மாத நன்கொடைகள் | | |
| `Giving.donations.void` | VOID | අවලංගුයි | ரத்து | | |
| `Giving.donations.fromDonorBook` | donor book | දායක පොත | நன்கொடையாளர் புத்தகம் | | |
| `Giving.donations.recordedBy` | Recorded by {name} | සටහන් කළේ {name} | பதிவு செய்தவர் {name} | | |
| `Giving.donations.receipt` | Receipt | රිසිට්පත | பற்றுச்சீட்டு | | |
| `Giving.donations.receiptNamed` | Receipt {receiptNo} | රිසිට්පත {receiptNo} | பற்றுச்சீட்டு {receiptNo} | | |
| `Giving.donations.voidNamed` | Void receipt {receiptNo} | රිසිට්පත {receiptNo} අවලංගු කරන්න | பற்றுச்சீட்டு {receiptNo} ஐ ரத்து செய் | | |
| `Giving.donations.voidAction` | Void | අවලංගු කරන්න | ரத்து செய் | | |
| `Giving.donations.recorded` | Receipt {receiptNo} recorded. | රිසිට්පත {receiptNo} සටහන් කළා. | பற்றுச்சீட்டு {receiptNo} பதிவு செய்யப்பட்டது. | | |
| `Giving.donations.voided` | Receipt {receiptNo} voided. | රිසිට්පත {receiptNo} අවලංගු කළා. | பற்றுச்சீட்டு {receiptNo} ரத்து செய்யப்பட்டது. | | |
| `Giving.record.title` | Record a gift | පරිත්‍යාගයක් සටහන් කරන්න | நன்கொடையைப் பதிவு செய் | | |
| `Giving.record.description` | The receipt number is given when you save. A gift cannot be changed afterwards, so check it first. | සුරකින විට රිසිට් අංකය ලැබේ. පසුව පරිත්‍යාගය වෙනස් කළ නොහැක, එබැවින් පළමුව පරීක්ෂා කරන්න. | சேமிக்கும்போது பற்றுச்சீட்டு எண் வழங்கப்படும். பின்னர் நன்கொடையை மாற்ற முடியாது, எனவே முதலில் சரிபார்க்கவும். | | |
| `Giving.record.kind` | What was given | දුන් දේ | வழங்கியது | | |
| `Giving.record.kindMoney` | Money | මුදල් | பணம் | | |
| `Giving.record.kindItems` | Items | ද්‍රව්‍ය | பொருட்கள் | | |
| `Giving.record.amount` | Amount (LKR) | මුදල (රු.) | தொகை (ரூ.) | | |
| `Giving.record.amountHint` | In rupees, for example 5000 or 2500.50. | රුපියල්වලින්, උදා: 5000 හෝ 2500.50. | ரூபாயில், எ.கா. 5000 அல்லது 2500.50. | | |
| `Giving.record.method` | Paid by | ගෙවූ ආකාරය | செலுத்திய முறை | | |
| `Giving.record.items` | What was given | දුන් දේ | வழங்கியது | | |
| `Giving.record.itemsHint` | For example: 50 exercise books, or a month of dry rations. This is on the receipt; it is never texted. | උදා: අභ්‍යාස පොත් 50, හෝ මසකට වියළි ආහාර. මෙය රිසිට්පතේ ඇත; කිසිදා කෙටි පණිවිඩයකින් නොයවයි. | எ.கா. 50 பயிற்சிப் புத்தகங்கள், அல்லது ஒரு மாத உலர் உணவுப் பொருட்கள். இது பற்றுச்சீட்டில் இருக்கும்; ஒருபோதும் குறுஞ்செய்தியாக அனுப்பப்படாது. | | |
| `Giving.record.quantity` | How many (optional) | ප්‍රමාණය (අත්‍යවශ්‍ය නැත) | எத்தனை (விருப்பம்) | | |
| `Giving.record.estimatedValue` | Worth about (LKR, optional) | ආසන්න වටිනාකම (රු., අත්‍යවශ්‍ය නැත) | ஏறத்தாழ மதிப்பு (ரூ., விருப்பம்) | | |
| `Giving.record.estimatedValueHint` | Only if the giver told you. | දෙන්නා ඔබට කීවොත් පමණි. | கொடுத்தவர் சொன்னால் மட்டும். | | |
| `Giving.record.date` | Day given | දුන් දිනය | வழங்கிய நாள் | | |
| `Giving.record.dateHint` | Today, or a day in the last year. | අද, නැතහොත් පසුගිය වසර තුළ දිනයක්. | இன்று, அல்லது கடந்த ஆண்டில் ஒரு நாள். | | |
| `Giving.record.who` | Who gave | දුන්නේ කවුද | வழங்கியவர் | | |
| `Giving.record.fromBook` | Someone in the donor book | දායක පොතේ සිටින කෙනෙක් | நன்கொடையாளர் புத்தகத்தில் உள்ள ஒருவர் | | |
| `Giving.record.walkIn` | Someone else | වෙනත් කෙනෙක් | வேறொருவர் | | |
| `Giving.record.findDonor` | Find them by name or phone | නමින් හෝ දුරකථනයෙන් සොයන්න | பெயர் அல்லது தொலைபேசி மூலம் தேடவும் | | |
| `Giving.record.findDonorHint` | Their name and phone come from the donor book, and the receipt is texted only if they agreed to texts. | ඔවුන්ගේ නම සහ දුරකථනය දායක පොතෙන් ලැබේ; ඔවුන් කෙටි පණිවිඩවලට එකඟ වූවා නම් පමණක් රිසිට්පත යවයි. | அவர்களின் பெயரும் தொலைபேசியும் நன்கொடையாளர் புத்தகத்திலிருந்து வரும்; குறுஞ்செய்திகளுக்கு அவர்கள் ஒப்புக்கொண்டிருந்தால் மட்டுமே பற்றுச்சீட்டு அனுப்பப்படும். | | |
| `Giving.record.chosen` | Chosen: {name} | තෝරා ඇත: {name} | தேர்ந்தெடுக்கப்பட்டது: {name} | | |
| `Giving.record.donorChoices` | People in the donor book | දායක පොතේ සිටින අය | நன்கொடையாளர் புத்தகத்தில் உள்ளவர்கள் | | |
| `Giving.record.donorName` | Their name | ඔවුන්ගේ නම | அவர்களின் பெயர் | | |
| `Giving.record.donorPhone` | Their phone (optional) | ඔවුන්ගේ දුරකථනය (අත්‍යවශ්‍ය නැත) | அவர்களின் தொலைபேசி (விருப்பம்) | | |
| `Giving.record.donorPhoneHint` | Kept on the receipt only. A phone typed here is never texted. | රිසිට්පතේ පමණක් තබයි. මෙහි ටයිප් කළ දුරකථනයට කිසිදා පණිවිඩ නොයවයි. | பற்றுச்சீட்டில் மட்டும் வைக்கப்படும். இங்கே தட்டச்சு செய்த தொலைபேசிக்கு ஒருபோதும் குறுஞ்செய்தி அனுப்பப்படாது. | | |
| `Giving.record.note` | Note (optional) | සටහන (අත්‍යවශ්‍ය නැත) | குறிப்பு (விருப்பம்) | | |
| `Giving.record.noteHint` | Printed on the receipt. | රිසිට්පතේ මුද්‍රණය වේ. | பற்றுச்சீட்டில் அச்சிடப்படும். | | |
| `Giving.record.cancel` | Cancel | අවලංගු කරන්න | ரத்து | | |
| `Giving.record.save` | Record the gift | පරිත්‍යාගය සටහන් කරන්න | நன்கொடையைப் பதிவு செய் | | |
| `Giving.record.errors.kind` | Choose money or items. | මුදල් හෝ ද්‍රව්‍ය තෝරන්න. | பணம் அல்லது பொருட்களைத் தேர்ந்தெடுக்கவும். | | |
| `Giving.record.errors.amount` | Enter an amount above zero, with at most two decimals. | ශුන්‍යයට වැඩි මුදලක් ඇතුළත් කරන්න, දශමස්ථාන දෙකක් දක්වා. | பூஜ்ஜியத்தை விட அதிகமான தொகையை, இரண்டு தசம இடங்கள் வரை உள்ளிடவும். | | |
| `Giving.record.errors.method` | Choose how it was paid. | ගෙවූ ආකාරය තෝරන්න. | செலுத்திய முறையைத் தேர்ந்தெடுக்கவும். | | |
| `Giving.record.errors.items` | Say what was given (up to 500 characters). | දුන් දේ කියන්න (අකුරු 500 දක්වා). | வழங்கியதைச் சொல்லவும் (500 எழுத்துகள் வரை). | | |
| `Giving.record.errors.quantity` | Enter a number above zero. | ශුන්‍යයට වැඩි සංඛ්‍යාවක් ඇතුළත් කරන්න. | பூஜ்ஜியத்தை விட அதிகமான எண்ணை உள்ளிடவும். | | |
| `Giving.record.errors.estimatedValue` | Enter an amount above zero, or leave it blank. | ශුන්‍යයට වැඩි මුදලක් ඇතුළත් කරන්න, නැතහොත් හිස්ව තබන්න. | பூஜ்ஜியத்தை விட அதிகமான தொகையை உள்ளிடவும், அல்லது காலியாக விடவும். | | |
| `Giving.record.errors.date` | Choose today or a day in the last year. | අද හෝ පසුගිය වසර තුළ දිනයක් තෝරන්න. | இன்று அல்லது கடந்த ஆண்டில் ஒரு நாளைத் தேர்ந்தெடுக்கவும். | | |
| `Giving.record.errors.donor` | Choose the person from the donor book. | දායක පොතෙන් පුද්ගලයා තෝරන්න. | நன்கொடையாளர் புத்தகத்திலிருந்து நபரைத் தேர்ந்தெடுக்கவும். | | |
| `Giving.record.errors.donorName` | Enter the giver's name. | දුන් අයගේ නම ඇතුළත් කරන්න. | வழங்கியவரின் பெயரை உள்ளிடவும். | | |
| `Giving.record.errors.donorPhone` | Enter a Sri Lankan phone number, or leave it blank. | ශ්‍රී ලාංකික දුරකථන අංකයක් ඇතුළත් කරන්න, නැතහොත් හිස්ව තබන්න. | இலங்கைத் தொலைபேசி எண்ணை உள்ளிடவும், அல்லது காலியாக விடவும். | | |
| `Giving.record.errors.note` | A note is at most 300 characters. | සටහනක් අකුරු 300 දක්වා විය හැක. | குறிப்பு 300 எழுத்துகள் வரை இருக்கலாம். | | |
| `Giving.void.title` | Void receipt {receiptNo} | රිසිට්පත {receiptNo} අවලංගු කරන්න | பற்றுச்சீட்டு {receiptNo} ஐ ரத்து செய் | | |
| `Giving.void.description` | {gift} from {name}. | {name} ගෙන් {gift}. | {name} இடமிருந்து {gift}. | | |
| `Giving.void.explain` | Voiding keeps the gift in the list, marked VOID, and takes it out of every total. It cannot be undone. If the gift was recorded wrongly, record it again afterwards. | අවලංගු කිරීමෙන් පරිත්‍යාගය ලැයිස්තුවේ අවලංගු ලෙස සලකුණු කර තබා සියලු එකතුවලින් ඉවත් කරයි. එය ආපසු හැරවිය නොහැක. පරිත්‍යාගය වැරදියට සටහන් කළා නම්, පසුව එය නැවත සටහන් කරන්න. | ரத்து செய்வது நன்கொடையை பட்டியலில் ரத்து எனக் குறித்து வைத்து, எல்லா மொத்தங்களிலிருந்தும் நீக்கும். இதைத் திரும்பப் பெற முடியாது. நன்கொடை தவறாகப் பதிவு செய்யப்பட்டிருந்தால், பின்னர் மீண்டும் பதிவு செய்யவும். | | |
| `Giving.void.reason` | Why is it voided? | අවලංගු කරන්නේ ඇයි? | ஏன் ரத்து செய்யப்படுகிறது? | | |
| `Giving.void.reasonHint` | The committee will read this. At most 300 characters. | කමිටුව මෙය කියවයි. අකුරු 300 දක්වා. | குழு இதைப் படிக்கும். 300 எழுத்துகள் வரை. | | |
| `Giving.void.reasonMissing` | Say why it is voided. | අවලංගු කරන්නේ ඇයිදැයි කියන්න. | ஏன் ரத்து செய்யப்படுகிறது என்று சொல்லவும். | | |
| `Giving.void.cancel` | Keep it | තබා ගන්න | வைத்திரு | | |
| `Giving.void.confirm` | Void the gift | පරිත්‍යාගය අවලංගු කරන්න | நன்கொடையை ரத்து செய் | | |
| `Giving.receipt.pageTitle` | Receipt | රිසිට්පත | பற்றுச்சீட்டு | | |
| `Giving.receipt.label` | Receipt {receiptNo} | රිසිට්පත {receiptNo} | பற்றுச்சீட்டு {receiptNo} | | |
| `Giving.receipt.signIn` | Sign in to see this receipt. | මෙම රිසිට්පත බැලීමට පිවිසෙන්න. | இந்தப் பற்றுச்சீட்டைப் பார்க்க உள்நுழையவும். | | |
| `Giving.receipt.signInLink` | Sign in | පිවිසෙන්න | உள்நுழை | | |
| `Giving.receipt.print` | Print or save as PDF | මුද්‍රණය කරන්න හෝ PDF ලෙස සුරකින්න | அச்சிடு அல்லது PDF ஆகச் சேமி | | |
| `Giving.receipt.backMine` | Back to my giving | මගේ පරිත්‍යාග වෙත | என் நன்கொடைகளுக்கு | | |
| `Giving.receipt.backList` | Back to donations | පරිත්‍යාග වෙත | நன்கொடைகளுக்கு | | |
| `Giving.receipt.void` | VOID | අවලංගුයි | ரத்து | | |
| `Giving.receipt.voidedOn` | Voided on {date} by {name}. | {date} දින {name} විසින් අවලංගු කළා. | {date} அன்று {name} ரத்து செய்தார். | | |
| `Giving.receipt.voidReason` | Reason: {reason} | හේතුව: {reason} | காரணம்: {reason} | | |
| `Giving.receipt.title` | Receipt | රිසිට්පත | பற்றுச்சீட்டு | | |
| `Giving.receipt.receivedFrom` | Received from | ලැබුණේ | பெற்றது | | |
| `Giving.receipt.amount` | Amount | මුදල | தொகை | | |
| `Giving.receipt.method` | Paid by | ගෙවූ ආකාරය | செலுத்திய முறை | | |
| `Giving.receipt.items` | Items | ද්‍රව්‍ය | பொருட்கள் | | |
| `Giving.receipt.quantity` | How many | ප්‍රමාණය | எத்தனை | | |
| `Giving.receipt.estimatedValue` | Worth about | ආසන්න වටිනාකම | ஏறத்தாழ மதிப்பு | | |
| `Giving.receipt.money` | LKR {amount} | රු. {amount} | ரூ. {amount} | | |
| `Giving.receipt.given` | Given on | දුන් දිනය | வழங்கிய நாள் | | |
| `Giving.receipt.note` | Note | සටහන | குறிப்பு | | |
| `Giving.receipt.recordedBy` | Recorded by | සටහන් කළේ | பதிவு செய்தவர் | | |
| `Giving.receipt.recordedOn` | {name}, {date} at {time} | {name}, {date} {time} | {name}, {date} {time} | | |
| `Giving.receipt.thanks` | With thanks for your gift. | ඔබේ පරිත්‍යාගයට ස්තූතියි. | உங்கள் நன்கொடைக்கு நன்றி. | | |
| `Giving.receipt.notTax` | An acknowledgement of a gift received, issued through Sethika. It is not a tax receipt. | Sethika හරහා නිකුත් කළ, ලැබුණු පරිත්‍යාගයක් පිළිගැනීමකි. මෙය බදු රිසිට්පතක් නොවේ. | Sethika வழியாக வழங்கப்பட்ட, பெற்ற நன்கொடைக்கான ஒப்புகை. இது வரிப் பற்றுச்சீட்டு அல்ல. | | |
| `Giving.mine.title` | My giving | මගේ පරිත්‍යාග | என் நன்கொடைகள் | | |
| `Giving.mine.intro` | The gifts institutions have recorded for you, with their receipts. | ආයතන ඔබ වෙනුවෙන් සටහන් කළ පරිත්‍යාග, ඒවායේ රිසිට්පත් සමඟ. | நிறுவனங்கள் உங்களுக்காகப் பதிவு செய்த நன்கொடைகள், அவற்றின் பற்றுச்சீட்டுகளுடன். | | |
| `Giving.mine.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயற்சிக்கவும் | | |
| `Giving.mine.none` | No gifts recorded for you yet. When an institution records your gift, its receipt appears here. | ඔබ වෙනුවෙන් තවම පරිත්‍යාග සටහන් කර නැත. ආයතනයක් ඔබේ පරිත්‍යාගය සටහන් කළ විට, එහි රිසිට්පත මෙහි දිස් වේ. | உங்களுக்காக இன்னும் நன்கொடைகள் பதிவு செய்யப்படவில்லை. ஒரு நிறுவனம் உங்கள் நன்கொடையைப் பதிவு செய்தால், அதன் பற்றுச்சீட்டு இங்கே தோன்றும். | | |
| `Giving.mine.list` | My gifts | මගේ පරිත්‍යාග | என் நன்கொடைகள் | | |
| `Giving.mine.void` | VOID | අවලංගුයි | ரத்து | | |
| `Giving.mine.receipt` | Receipt | රිසිට්පත | பற்றுச்சீட்டு | | |
| `Giving.mine.receiptNamed` | Receipt {receiptNo} | රිසිට්පත {receiptNo} | பற்றுச்சீட்டு {receiptNo} | | |
| `Shell.groups.give` | My giving | මගේ පරිත්‍යාග | என் நன்கொடைகள் | | |
| `Shell.groups.giving` | Giving | පරිත්‍යාග | நன்கொடைகள் | | |
| `Shell.items.myGiving` | My giving | මගේ පරිත්‍යාග | என் நன்கொடைகள் | | |
| `Shell.items.donations` | Donations and receipts | පරිත්‍යාග සහ රිසිට්පත් | நன்கொடைகளும் பற்றுச்சீட்டுகளும் | | |
| `Admin.institutions.sync` | Bring roles up to date | භූමිකා යාවත්කාලීන කරන්න | பங்குகளைப் புதுப்பிக்கவும் | | |
| `Admin.institutions.syncNamed` | Bring {name}'s roles up to date | {name} හි භූමිකා යාවත්කාලීන කරන්න | {name} இன் பங்குகளைப் புதுப்பிக்கவும் | | |
| `Admin.institutions.synced` | {name}: {modules, plural, =0 {no module} one {# module} other {# modules}}, {roles, plural, =0 {no role} one {# role} other {# roles}} and {grants, plural, =0 {no permission} one {# permission} other {# permissions}} added. | {name}: මොඩියුල {modules}ක්, භූමිකා {roles}ක් සහ අවසර {grants}ක් එක් කළා. | {name}: {modules} தொகுதிகள், {roles} பங்குகள், {grants} அனுமதிகள் சேர்க்கப்பட்டன. | | |
| `Admin.institutions.syncedNothing` | {name} already holds everything its templates give. Nothing was added. | {name} සතුව එහි ආකෘති දෙන සියල්ල දැනටමත් ඇත. කිසිවක් එක් නොකළා. | {name} இன் வார்ப்புருக்கள் தருவது அனைத்தும் ஏற்கனவே உள்ளது. எதுவும் சேர்க்கப்படவில்லை. | | |

## Giving lists (2026-11-04, `core.LookupValues`, plan 09 M20)

| List / code | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `PAYMENT_METHOD.CASH` | Cash | මුදල් | ரொக்கம் | | |
| `PAYMENT_METHOD.BANK_TRANSFER` | Bank transfer | බැංකු මාරුව | வங்கிப் பரிமாற்றம் | | |
| `PAYMENT_METHOD.CHEQUE` | Cheque | චෙක්පත | காசோலை | | |
| `PAYMENT_METHOD.CARD` | Card | කාඩ්පත | அட்டை | | |
| `PAYMENT_METHOD.PAYHERE` | Online (PayHere) | මාර්ගගත (PayHere) | இணையவழி (PayHere) | | |
| `NEED_CATEGORY.DRY_RATIONS` | Dry rations | වියළි ආහාර | உலர் உணவுப் பொருட்கள் | | |
| `NEED_CATEGORY.STATIONERY` | Stationery | ලිපි ද්‍රව්‍ය | எழுதுபொருட்கள் | | |
| `NEED_CATEGORY.TOILETRIES` | Toiletries | සනීපාරක්ෂක ද්‍රව්‍ය | கழிப்பறைப் பொருட்கள் | | |
| `NEED_CATEGORY.MEDICINE` | Medicine | ඖෂධ | மருந்துகள் | | |
| `NEED_CATEGORY.CLOTHING` | Clothing | ඇඳුම් | ஆடைகள் | | |
| `NEED_CATEGORY.KITCHEN_CLEANING` | Kitchen and cleaning | මුළුතැන්ගෙයි සහ පිරිසිදු කිරීමේ ද්‍රව්‍ය | சமையலறை மற்றும் சுத்தம் செய்யும் பொருட்கள் | | |
| `NEED_CATEGORY.OTHER` | Other | වෙනත් | மற்றவை | | |
| `UNIT.PIECES` | Pieces | කෑලි | துண்டுகள் | | |
| `UNIT.PACKETS` | Packets | පැකට් | பொதிகள் | | |
| `UNIT.KG` | Kilograms | කිලෝග්‍රෑම් | கிலோகிராம் | | |
| `UNIT.LITRES` | Litres | ලීටර් | லிட்டர் | | |
| `UNIT.BOXES` | Boxes | පෙට්ටි | பெட்டிகள் | | |
| `UNIT.SETS` | Sets | කට්ටල | தொகுப்புகள் | | |

## Receipt texts (2026-11-04, `DONATION_RECEIPT`, `DONATION_RECEIPT_ITEMS`, plan 09 M5)

| Template | English | Sinhala (draft) | Tamil (draft) | Reviewer (si) | Reviewer (ta) |
|---|---|---|---|---|---|
| `DONATION_RECEIPT` | Sethika {receiptNo}: {institution}, LKR {amount}. Your gift was received. Thank you. | Sethika {receiptNo}: {institution}, රු. {amount}. ඔබේ පරිත්‍යාගය ලැබුණා. ස්තූතියි. | Sethika {receiptNo}: {institution}, ரூ. {amount}. உங்கள் நன்கொடை கிடைத்தது. நன்றி. | | |
| `DONATION_RECEIPT_ITEMS` | Sethika {receiptNo}: {institution}. Your gift of items was received. Thank you. | Sethika {receiptNo}: {institution}. ඔබේ ද්‍රව්‍ය පරිත්‍යාගය ලැබුණා. ස්තූතියි. | Sethika {receiptNo}: {institution}. உங்கள் பொருள் நன்கொடை கிடைத்தது. நன்றி. | | |

## Giving: needs and pledges (2026-11-05, `Giving`, `Shell`, plan 09 D.2)

Drafts by Claude, not reviewed by a Sinhala or Tamil speaker. "Pledge" is written පොරොන්දුව / உறுதிமொழி and "need" අවශ්‍යතාව / தேவை throughout.

| Key | English | Sinhala | Tamil |
|---|---|---|---|
| `Giving.errors.need_not_found` | There is no such need here. It may have been taken down. | මෙහි එවැනි අවශ්‍යතාවක් නැත. එය ඉවත් කර තිබෙන්නට පුළුවන. | இங்கு அப்படி ஒரு தேவை இல்லை. அது நீக்கப்பட்டிருக்கலாம். |
| `Giving.errors.pledge_not_found` | There is no such pledge here. | මෙහි එවැනි පොරොන්දුවක් නැත. | இங்கு அப்படி ஒரு உறுதிமொழி இல்லை. |
| `Giving.errors.need_full` | There is not that much left of this need: others pledged first. Look again and pledge what is left. | මෙම අවශ්‍යතාවෙන් ඒ තරම් ප්‍රමාණයක් ඉතිරි නැත: අන් අය කලින් පොරොන්දු වී ඇත. නැවත බලා ඉතිරි ප්‍රමාණය පොරොන්දු වන්න. | இந்தத் தேவையில் அவ்வளவு மீதமில்லை: மற்றவர்கள் முன்பே உறுதியளித்துவிட்டார்கள். மீண்டும் பார்த்து மீதமுள்ளதை உறுதியளியுங்கள். |
| `Giving.errors.need_closed` | This need is no longer taking anything. | මෙම අවශ්‍යතාව තවදුරටත් කිසිවක් භාර ගන්නේ නැත. | இந்தத் தேவை இனி எதையும் ஏற்காது. |
| `Giving.errors.need_wrong_status` | This need cannot take that step now. Look at the list again. | මේ අවස්ථාවේ මෙම අවශ්‍යතාවට එම පියවර ගත නොහැක. ලැයිස්තුව නැවත බලන්න. | இப்போது இந்தத் தேவையில் அந்தப் படியை எடுக்க முடியாது. பட்டியலை மீண்டும் பாருங்கள். |
| `Giving.errors.quantity_below_pledged` | That is less than people have already pledged or brought. Cancel a pledge first if the need really is smaller. | එය දැනටමත් පොරොන්දු වූ හෝ ලැබුණු ප්‍රමාණයට වඩා අඩුය. අවශ්‍යතාව ඇත්තටම අඩු නම් පළමුව පොරොන්දුවක් අවලංගු කරන්න. | அது ஏற்கெனவே உறுதியளிக்கப்பட்ட அல்லது பெறப்பட்ட அளவை விடக் குறைவு. தேவை உண்மையில் குறைந்திருந்தால் முதலில் ஓர் உறுதிமொழியை ரத்து செய்யுங்கள். |
| `Giving.errors.open_pledges` | People still have open pledges on this need. Mark each one delivered or cancel it first. | මෙම අවශ්‍යතාවට තවමත් විවෘත පොරොන්දු තිබේ. පළමුව එක් එක් පොරොන්දුව ලැබුණු බව සටහන් කරන්න හෝ අවලංගු කරන්න. | இந்தத் தேவையில் இன்னும் திறந்த உறுதிமொழிகள் உள்ளன. முதலில் ஒவ்வொன்றையும் பெறப்பட்டதாகக் குறியுங்கள் அல்லது ரத்து செய்யுங்கள். |
| `Giving.errors.need_has_gifts` | Gifts have come for this need, so it cannot be cancelled. Close it instead: what came stays counted. | මෙම අවශ්‍යතාවට පරිත්‍යාග ලැබී ඇති නිසා එය අවලංගු කළ නොහැක. ඒ වෙනුවට එය වසන්න: ලැබුණු දේ ගණනට ඉතිරි වේ. | இந்தத் தேவைக்கு நன்கொடைகள் வந்துள்ளதால் இதை ரத்து செய்ய முடியாது. பதிலாக மூடுங்கள்: வந்தவை கணக்கில் இருக்கும். |
| `Giving.errors.pledge_not_open` | This pledge is already delivered or cancelled. | මෙම පොරොන්දුව දැනටමත් ලැබී හෝ අවලංගු වී ඇත. | இந்த உறுதிமொழி ஏற்கெனவே பெறப்பட்டது அல்லது ரத்து செய்யப்பட்டது. |
| `Giving.errors.too_many_pledges` | You already have 5 open pledges on this need. Deliver or cancel one first. | මෙම අවශ්‍යතාවට ඔබට දැනටමත් විවෘත පොරොන්දු 5ක් තිබේ. පළමුව එකක් ලබා දෙන්න හෝ අවලංගු කරන්න. | இந்தத் தேவையில் உங்களிடம் ஏற்கெனவே 5 திறந்த உறுதிமொழிகள் உள்ளன. முதலில் ஒன்றை வழங்குங்கள் அல்லது ரத்து செய்யுங்கள். |
| `Giving.progress.label` | Progress | ප්‍රගතිය | முன்னேற்றம் |
| `Giving.progress.figures` | {received} received, {pledged} spoken for, of {needed} {unit} | {unit} {needed}න් {received}ක් ලැබී ඇත, {pledged}ක් වෙන් වී ඇත | {needed} {unit} இல் {received} பெறப்பட்டது, {pledged} ஒதுக்கப்பட்டது |
| `Giving.priority.Low` | Low | අඩු | குறைவு |
| `Giving.priority.Normal` | Normal | සාමාන්‍ය | சாதாரணம் |
| `Giving.priority.Urgent` | Urgent | හදිසි | அவசரம் |
| `Giving.board.title` | Needs board | අවශ්‍යතා පුවරුව | தேவைகள் பலகை |
| `Giving.board.intro` | What institutions are asking for right now. Pledge what you can bring, and the institution records it when it arrives. | ආයතන මේ මොහොතේ ඉල්ලන දේ. ඔබට ගෙන ආ හැකි දේ පොරොන්දු වන්න; එය ලැබුණු විට ආයතනය එය සටහන් කරයි. | நிறுவனங்கள் இப்போது கேட்பவை. உங்களால் கொண்டு வர முடிந்ததை உறுதியளியுங்கள்; அது வந்ததும் நிறுவனம் பதிவு செய்யும். |
| `Giving.board.filters` | Narrow the board | පුවරුව පටු කරන්න | பலகையைச் சுருக்குங்கள் |
| `Giving.board.district` | District | දිස්ත්‍රික්කය | மாவட்டம் |
| `Giving.board.type` | Kind of place | ස්ථාන වර්ගය | இட வகை |
| `Giving.board.category` | Category | ප්‍රවර්ගය | பிரிவு |
| `Giving.board.all` | All | සියල්ල | அனைத்தும் |
| `Giving.board.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் |
| `Giving.board.none` | No institution has an open need right now. | දැනට කිසිම ආයතනයකට විවෘත අවශ්‍යතාවක් නැත. | இப்போது எந்த நிறுவனத்திலும் திறந்த தேவை இல்லை. |
| `Giving.board.noMatch` | No open need matches what you chose. | ඔබ තෝරාගත් දෙයට ගැළපෙන විවෘත අවශ්‍යතාවක් නැත. | நீங்கள் தேர்ந்தெடுத்ததற்குப் பொருந்தும் திறந்த தேவை இல்லை. |
| `Giving.board.list` | Open needs | විවෘත අවශ්‍යතා | திறந்த தேவைகள் |
| `Giving.board.deadline` | By {date} | {date} වන විට | {date} க்குள் |
| `Giving.board.pledge` | Pledge | පොරොන්දු වන්න | உறுதியளி |
| `Giving.board.pledgeNamed` | Pledge to {need} | {need} සඳහා පොරොන්දු වන්න | {need} க்கு உறுதியளி |
| `Giving.board.allPledged` | All of this is pledged. Thank you. | මේ සියල්ල පොරොන්දු වී ඇත. ස්තුතියි. | இவை அனைத்தும் உறுதியளிக்கப்பட்டுள்ளன. நன்றி. |
| `Giving.board.pledged` | Your pledge is recorded. Thank you. | ඔබේ පොරොන්දුව සටහන් විය. ස්තුතියි. | உங்கள் உறுதிமொழி பதிவானது. நன்றி. |
| `Giving.board.onPage` | What we need | අපට අවශ්‍ය දේ | எங்களுக்குத் தேவையானவை |
| `Giving.board.onPageIntro` | Pledge what you can bring. | ඔබට ගෙන ආ හැකි දේ පොරොන්දු වන්න. | உங்களால் கொண்டு வர முடிந்ததை உறுதியளியுங்கள். |
| `Giving.pledge.title` | Pledge to {need} | {need} සඳහා පොරොන්දු වන්න | {need} க்கு உறுதியளி |
| `Giving.pledge.left` | {count} {unit} still needed. | තවත් {unit} {count}ක් අවශ්‍යයි. | இன்னும் {count} {unit} தேவை. |
| `Giving.pledge.signInFirst` | Sign in to pledge, so the institution knows who is coming. You will come back to this need. | පොරොන්දු වීමට පුරනය වන්න, එවිට එන්නේ කවුදැයි ආයතනය දනී. ඔබ නැවත මෙම අවශ්‍යතාවටම පැමිණේවි. | உறுதியளிக்க உள்நுழையுங்கள்; அப்போது யார் வருகிறார் என்று நிறுவனம் அறியும். நீங்கள் இந்தத் தேவைக்கே திரும்புவீர்கள். |
| `Giving.pledge.institutionSession` | You are working in an institution. Pledges are made from your own account. | ඔබ දැන් ආයතනයක වැඩ කරයි. පොරොන්දු වන්නේ ඔබේම ගිණුමෙනි. | நீங்கள் ஒரு நிறுவனத்தில் பணிபுரிகிறீர்கள். உறுதிமொழிகள் உங்கள் சொந்தக் கணக்கிலிருந்து அளிக்கப்படும். |
| `Giving.pledge.notAllowed` | This account cannot pledge. | මෙම ගිණුමට පොරොන්දු විය නොහැක. | இந்தக் கணக்கால் உறுதியளிக்க முடியாது. |
| `Giving.pledge.quantity` | How many ({unit}) | ප්‍රමාණය ({unit}) | எத்தனை ({unit}) |
| `Giving.pledge.quantityInvalid` | Enter a number above zero, with at most two decimals. | බිංදුවට වැඩි, දශමස්ථාන දෙකකට නොවැඩි අංකයක් ඇතුළත් කරන්න. | பூஜ்ஜியத்தை விட அதிகமான, இரண்டு தசம இடங்களுக்கு மிகாத எண்ணை உள்ளிடுங்கள். |
| `Giving.pledge.expected` | When you expect to bring it (optional) | ගෙන ඒමට බලාපොරොත්තු වන දිනය (අත්‍යවශ්‍ය නොවේ) | கொண்டு வர எதிர்பார்க்கும் நாள் (விருப்பத்திற்குரியது) |
| `Giving.pledge.expectedHint` | This helps the institution plan. | මෙය ආයතනයට සැලසුම් කිරීමට උදව් වේ. | இது நிறுவனம் திட்டமிட உதவும். |
| `Giving.pledge.expectedInvalid` | Choose today or a day within the next year. | අද හෝ ඉදිරි වසර තුළ දිනයක් තෝරන්න. | இன்று அல்லது அடுத்த ஓராண்டுக்குள் ஒரு நாளைத் தேர்ந்தெடுங்கள். |
| `Giving.pledge.cancel` | Not now | දැන් නොවේ | இப்போது வேண்டாம் |
| `Giving.pledge.save` | Pledge | පොරොන්දු වන්න | உறுதியளி |
| `Giving.needs.title` | Needs | අවශ්‍යතා | தேவைகள் |
| `Giving.needs.intro` | Ask for what your institution needs. A need stays a draft until you open it onto the public board, and it tracks what is pledged and what arrives. | ඔබේ ආයතනයට අවශ්‍ය දේ ඉල්ලන්න. පොදු පුවරුවට විවෘත කරන තුරු අවශ්‍යතාව කෙටුම්පතක් ලෙස පවතින අතර, පොරොන්දු වූ දේත් ලැබුණු දේත් එහි සටහන් වේ. | உங்கள் நிறுவனத்துக்குத் தேவையானதைக் கேளுங்கள். பொதுப் பலகையில் திறக்கும் வரை தேவை வரைவாகவே இருக்கும்; உறுதியளிக்கப்பட்டதும் வந்ததும் அதில் கணக்கிடப்படும். |
| `Giving.needs.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயலுங்கள் |
| `Giving.needs.add` | Add a need | අවශ්‍යතාවක් එක් කරන්න | தேவையைச் சேர் |
| `Giving.needs.none` | No needs yet. | තවම අවශ්‍යතා නැත. | இன்னும் தேவைகள் இல்லை. |
| `Giving.needs.list` | Your needs | ඔබේ අවශ්‍යතා | உங்கள் தேவைகள் |
| `Giving.needs.deadline` | By {date} | {date} වන විට | {date} க்குள் |
| `Giving.needs.status.Draft` | Draft | කෙටුම්පත | வரைவு |
| `Giving.needs.status.Open` | Open | විවෘතයි | திறந்துள்ளது |
| `Giving.needs.status.Fulfilled` | Fulfilled | සම්පූර්ණයි | நிறைவேறியது |
| `Giving.needs.status.Closed` | Closed | වසා ඇත | மூடப்பட்டது |
| `Giving.needs.status.Cancelled` | Cancelled | අවලංගුයි | ரத்து செய்யப்பட்டது |
| `Giving.needs.pledges` | Pledges ({count}) | පොරොන්දු ({count}) | உறுதிமொழிகள் ({count}) |
| `Giving.needs.pledgesNamed` | Pledges on {need} | {need} සඳහා පොරොන්දු | {need} இன் உறுதிமொழிகள் |
| `Giving.needs.open` | Open on the board | පුවරුවට විවෘත කරන්න | பலகையில் திற |
| `Giving.needs.openNamed` | Open {need} on the board | {need} පුවරුවට විවෘත කරන්න | {need} ஐப் பலகையில் திற |
| `Giving.needs.edit` | Change | වෙනස් කරන්න | மாற்று |
| `Giving.needs.editNamed` | Change {need} | {need} වෙනස් කරන්න | {need} ஐ மாற்று |
| `Giving.needs.close` | Close | වසන්න | மூடு |
| `Giving.needs.closeNamed` | Close {need} | {need} වසන්න | {need} ஐ மூடு |
| `Giving.needs.cancel` | Cancel | අවලංගු කරන්න | ரத்து செய் |
| `Giving.needs.cancelNamed` | Cancel {need} | {need} අවලංගු කරන්න | {need} ஐ ரத்து செய் |
| `Giving.needs.added` | Need added as a draft. | අවශ්‍යතාව කෙටුම්පතක් ලෙස එක් විය. | தேவை வரைவாகச் சேர்க்கப்பட்டது. |
| `Giving.needs.saved` | Need changed. | අවශ්‍යතාව වෙනස් විය. | தேவை மாற்றப்பட்டது. |
| `Giving.needs.stepped.open` | {need} is on the board. | {need} පුවරුවේ ඇත. | {need} பலகையில் உள்ளது. |
| `Giving.needs.stepped.close` | {need} is closed. | {need} වසා ඇත. | {need} மூடப்பட்டது. |
| `Giving.needs.stepped.cancel` | {need} is cancelled. | {need} අවලංගු විය. | {need} ரத்து செய்யப்பட்டது. |
| `Giving.needForm.addTitle` | Add a need | අවශ්‍යතාවක් එක් කරන්න | தேவையைச் சேர் |
| `Giving.needForm.addDescription` | It stays a draft, seen by nobody, until you open it. | ඔබ විවෘත කරන තුරු එය කෙටුම්පතක් ලෙස පවතින අතර කිසිවෙකුට නොපෙනේ. | நீங்கள் திறக்கும் வரை இது யாருக்கும் தெரியாத வரைவாக இருக்கும். |
| `Giving.needForm.editTitle` | Change this need | මෙම අවශ්‍යතාව වෙනස් කරන්න | இந்தத் தேவையை மாற்று |
| `Giving.needForm.editDescription` | The quantity cannot go below what is already pledged or received. | දැනටමත් පොරොන්දු වූ හෝ ලැබුණු ප්‍රමාණයට වඩා අඩු කළ නොහැක. | ஏற்கெனவே உறுதியளிக்கப்பட்ட அல்லது பெறப்பட்ட அளவை விடக் குறைக்க முடியாது. |
| `Giving.needForm.title` | What is needed | අවශ්‍ය දේ | என்ன தேவை |
| `Giving.needForm.titleHint` | For example: school books for Grade 5. | උදාහරණයක් ලෙස: 5 ශ්‍රේණියට පාසල් පොත්. | உதாரணமாக: தரம் 5 க்கான பாடசாலைப் புத்தகங்கள். |
| `Giving.needForm.description` | More about it (optional) | ඒ ගැන වැඩි විස්තර (අත්‍යවශ්‍ය නොවේ) | மேலும் விவரம் (விருப்பத்திற்குரியது) |
| `Giving.needForm.category` | Category | ප්‍රවර්ගය | பிரிவு |
| `Giving.needForm.quantity` | How many | ප්‍රමාණය | எத்தனை |
| `Giving.needForm.unit` | Counted in | ගණන් කරන ඒකකය | அளவீடு |
| `Giving.needForm.priority` | Priority | ප්‍රමුඛතාව | முன்னுரிமை |
| `Giving.needForm.deadline` | Needed by (optional) | අවශ්‍ය දිනය (අත්‍යවශ්‍ය නොවේ) | தேவைப்படும் நாள் (விருப்பத்திற்குரியது) |
| `Giving.needForm.cancel` | Cancel | අවලංගු කරන්න | ரத்து செய் |
| `Giving.needForm.saveAdd` | Add the need | අවශ්‍යතාව එක් කරන්න | தேவையைச் சேர் |
| `Giving.needForm.saveEdit` | Save | සුරකින්න | சேமி |
| `Giving.needForm.errors.title` | Say what is needed (up to 150 characters). | අවශ්‍ය දේ කියන්න (අකුරු 150 දක්වා). | என்ன தேவை என்று சொல்லுங்கள் (150 எழுத்துகள் வரை). |
| `Giving.needForm.errors.description` | At most 1,000 characters. | අකුරු 1,000කට නොවැඩි විය යුතුය. | அதிகபட்சம் 1,000 எழுத்துகள். |
| `Giving.needForm.errors.category` | Choose a category. | ප්‍රවර්ගයක් තෝරන්න. | ஒரு பிரிவைத் தேர்ந்தெடுங்கள். |
| `Giving.needForm.errors.unit` | The unit cannot change once something is pledged. | යමක් පොරොන්දු වූ පසු ඒකකය වෙනස් කළ නොහැක. | ஏதாவது உறுதியளிக்கப்பட்ட பின் அளவீட்டை மாற்ற முடியாது. |
| `Giving.needForm.errors.quantity` | Enter a number above zero, with at most two decimals. | බිංදුවට වැඩි, දශමස්ථාන දෙකකට නොවැඩි අංකයක් ඇතුළත් කරන්න. | பூஜ்ஜியத்தை விட அதிகமான, இரண்டு தசம இடங்களுக்கு மிகாத எண்ணை உள்ளிடுங்கள். |
| `Giving.needForm.errors.priority` | Choose a priority. | ප්‍රමුඛතාවක් තෝරන්න. | முன்னுரிமையைத் தேர்ந்தெடுங்கள். |
| `Giving.needForm.errors.deadline` | Choose today or a later day. | අද හෝ පසු දිනයක් තෝරන්න. | இன்று அல்லது பிந்திய நாளைத் தேர்ந்தெடுங்கள். |
| `Giving.pledges.title` | Pledges on {need} | {need} සඳහා පොරොන්දු | {need} இன் உறுதிமொழிகள் |
| `Giving.pledges.description` | Who promised what, and what became of it. | කවුරු කුමක් පොරොන්දු වූවාද, එය කෙසේ අවසන් වූවාද. | யார் என்ன உறுதியளித்தார், அது என்ன ஆனது. |
| `Giving.pledges.none` | No pledges yet. | තවම පොරොන්දු නැත. | இன்னும் உறுதிமொழிகள் இல்லை. |
| `Giving.pledges.list` | Pledges | පොරොන්දු | உறுதிமொழிகள் |
| `Giving.pledges.status.Pledged` | Waiting | බලාපොරොත්තුවෙන් | காத்திருக்கிறது |
| `Giving.pledges.status.Delivered` | Delivered | ලැබුණි | பெறப்பட்டது |
| `Giving.pledges.status.Cancelled` | Cancelled | අවලංගුයි | ரத்து செய்யப்பட்டது |
| `Giving.pledges.promised` | Promised {count} {unit} | {unit} {count}ක් පොරොන්දු විය | {count} {unit} உறுதியளித்தது |
| `Giving.pledges.expected` | expected {date} | බලාපොරොත්තු දිනය {date} | எதிர்பார்க்கும் நாள் {date} |
| `Giving.pledges.came` | {count} {unit} came | {unit} {count}ක් ලැබුණි | {count} {unit} வந்தது |
| `Giving.pledges.reasonShown` | Reason: {reason} | හේතුව: {reason} | காரணம்: {reason} |
| `Giving.pledges.receiptNamed` | Receipt {receiptNo} | රිසිට්පත {receiptNo} | பற்றுச்சீட்டு {receiptNo} |
| `Giving.pledges.receive` | Mark delivered | ලැබුණු බව සටහන් කරන්න | பெறப்பட்டதாகக் குறி |
| `Giving.pledges.receiveNamed` | Mark {name}'s pledge delivered | {name}ගේ පොරොන්දුව ලැබුණු බව සටහන් කරන්න | {name} இன் உறுதிமொழியைப் பெறப்பட்டதாகக் குறி |
| `Giving.pledges.cancelPledge` | Cancel | අවලංගු කරන්න | ரத்து செய் |
| `Giving.pledges.cancelNamed` | Cancel {name}'s pledge | {name}ගේ පොරොන්දුව අවලංගු කරන්න | {name} இன் உறுதிமொழியை ரத்து செய் |
| `Giving.pledges.close` | Close | වසන්න | மூடு |
| `Giving.pledges.back` | Back | ආපසු | பின்செல் |
| `Giving.pledges.received` | Delivered. Receipt {receiptNo} recorded. | ලැබුණි. රිසිට්පත {receiptNo} සටහන් විය. | பெறப்பட்டது. பற்றுச்சீட்டு {receiptNo} பதிவானது. |
| `Giving.pledges.cancelled` | Pledge cancelled. | පොරොන්දුව අවලංගු විය. | உறுதிமொழி ரத்து செய்யப்பட்டது. |
| `Giving.pledges.receiveTitle` | What {name} brought | {name} ගෙනා දේ | {name} கொண்டு வந்தவை |
| `Giving.pledges.receiveDescription` | They promised {count} {unit}. This records a receipt, which cannot be changed afterwards. | ඔවුන් {unit} {count}ක් පොරොන්දු විය. මෙයින් රිසිට්පතක් සටහන් වන අතර පසුව එය වෙනස් කළ නොහැක. | அவர் {count} {unit} உறுதியளித்தார். இது பற்றுச்சீட்டைப் பதிவு செய்யும்; பின்னர் அதை மாற்ற முடியாது. |
| `Giving.pledges.cameLabel` | How many came ({unit}) | ලැබුණු ප්‍රමාණය ({unit}) | எத்தனை வந்தது ({unit}) |
| `Giving.pledges.cameHint` | What did not come goes back to the need. | නොලැබුණු ප්‍රමාණය නැවත අවශ්‍යතාවට එක් වේ. | வராதது மீண்டும் தேவைக்குச் சேரும். |
| `Giving.pledges.cameInvalid` | Enter a number above zero, at most {count}. | බිංදුවට වැඩි, {count}ට නොවැඩි අංකයක් ඇතුළත් කරන්න. | பூஜ்ஜியத்தை விட அதிகமான, {count} க்கு மிகாத எண்ணை உள்ளிடுங்கள். |
| `Giving.pledges.receiveConfirm` | Record the delivery | ලැබීම සටහන් කරන්න | பெறுதலைப் பதிவு செய் |
| `Giving.pledges.cancelTitle` | Cancel {name}'s pledge | {name}ගේ පොරොන්දුව අවලංගු කරන්න | {name} இன் உறுதிமொழியை ரத்து செய் |
| `Giving.pledges.cancelDescription` | What they promised goes back to the need. They will see your reason in My giving. | ඔවුන් පොරොන්දු වූ ප්‍රමාණය නැවත අවශ්‍යතාවට එක් වේ. ඔබේ හේතුව ඔවුන්ට මගේ පරිත්‍යාග තුළ පෙනේ. | அவர் உறுதியளித்தது மீண்டும் தேவைக்குச் சேரும். உங்கள் காரணத்தை அவர் எனது கொடைகளில் காண்பார். |
| `Giving.pledges.reason` | Why is it cancelled? | අවලංගු කරන්නේ ඇයි? | ஏன் ரத்து செய்யப்படுகிறது? |
| `Giving.pledges.reasonMissing` | Say why it is cancelled. | අවලංගු කරන හේතුව කියන්න. | ரத்து செய்வதற்கான காரணத்தைச் சொல்லுங்கள். |
| `Giving.pledges.cancelConfirm` | Cancel the pledge | පොරොන්දුව අවලංගු කරන්න | உறுதிமொழியை ரத்து செய் |
| `Giving.myPledges.title` | My pledges | මගේ පොරොන්දු | எனது உறுதிமொழிகள் |
| `Giving.myPledges.none` | You have not pledged to any need yet. | ඔබ තවම කිසිම අවශ්‍යතාවකට පොරොන්දු වී නැත. | நீங்கள் இன்னும் எந்தத் தேவைக்கும் உறுதியளிக்கவில்லை. |
| `Giving.myPledges.seeBoard` | See the needs board | අවශ්‍යතා පුවරුව බලන්න | தேவைகள் பலகையைப் பாருங்கள் |
| `Giving.myPledges.list` | My pledges | මගේ පොරොන්දු | எனது உறுதிமொழிகள் |
| `Giving.myPledges.status.Pledged` | Waiting for delivery | ලබා දීමට බලාපොරොත්තුවෙන් | வழங்கக் காத்திருக்கிறது |
| `Giving.myPledges.status.Delivered` | Delivered | ලැබුණි | பெறப்பட்டது |
| `Giving.myPledges.status.Cancelled` | Cancelled | අවලංගුයි | ரத்து செய்யப்பட்டது |
| `Giving.myPledges.promised` | Promised {count} {unit} | {unit} {count}ක් පොරොන්දු විය | {count} {unit} உறுதியளித்தது |
| `Giving.myPledges.expected` | expected {date} | බලාපොරොත්තු දිනය {date} | எதிர்பார்க்கும் நாள் {date} |
| `Giving.myPledges.came` | {count} {unit} received | {unit} {count}ක් ලැබුණි | {count} {unit} பெறப்பட்டது |
| `Giving.myPledges.reasonShown` | Reason: {reason} | හේතුව: {reason} | காரணம்: {reason} |
| `Giving.myPledges.receipt` | Receipt | රිසිට්පත | பற்றுச்சீட்டு |
| `Giving.myPledges.receiptNamed` | Receipt {receiptNo} | රිසිට්පත {receiptNo} | பற்றுச்சீட்டு {receiptNo} |
| `Giving.myPledges.cancel` | Cancel my pledge | මගේ පොරොන්දුව අවලංගු කරන්න | எனது உறுதிமொழியை ரத்து செய் |
| `Giving.myPledges.cancelNamed` | Cancel my pledge to {need} | {need} සඳහා මගේ පොරොන්දුව අවලංගු කරන්න | {need} க்கான எனது உறுதிமொழியை ரத்து செய் |
| `Giving.myPledges.cancelled` | Your pledge is cancelled. | ඔබේ පොරොන්දුව අවලංගු විය. | உங்கள் உறுதிமொழி ரத்து செய்யப்பட்டது. |
| `Giving.record.need` | For a need (optional) | අවශ්‍යතාවක් සඳහා (අත්‍යවශ්‍ය නොවේ) | ஒரு தேவைக்காக (விருப்பத்திற்குரியது) |
| `Giving.record.needHint` | Items count toward the need, so say how many. | භාණ්ඩ අවශ්‍යතාවට ගණන් වේ, ඒ නිසා ප්‍රමාණය කියන්න. | பொருட்கள் தேவையில் கணக்கிடப்படும், எனவே எத்தனை என்று சொல்லுங்கள். |
| `Giving.record.needNone` | Not for a need | අවශ්‍යතාවක් සඳහා නොවේ | தேவைக்காக அல்ல |
| `Shell.items.needsBoard` | Needs board | අවශ්‍යතා පුවරුව | தேவைகள் பலகை |
| `Shell.items.needs` | Needs | අවශ්‍යතා | தேவைகள் |
| `Shell.public.needs` | Needs board | අවශ්‍යතා පුවරුව | தேவைகள் பலகை |
| `Giving.give.heading` | Give money | මුදල් පරිත්‍යාග කරන්න | பணம் நன்கொடை அளியுங்கள் |
| `Giving.give.intro` | Your gift goes straight to {name} through PayHere. Sethika never holds it. | ඔබේ පරිත්‍යාගය PayHere හරහා කෙලින්ම {name} වෙත යයි. Sethika එය කිසිවිටෙක තබා නොගනී. | உங்கள் நன்கொடை PayHere வழியாக நேரடியாக {name} க்குச் செல்லும். Sethika அதை ஒருபோதும் வைத்திருப்பதில்லை. |
| `Giving.give.open` | Give online | මාර්ගගතව පරිත්‍යාග කරන්න | இணையவழியில் நன்கொடை அளியுங்கள் |
| `Giving.give.openNeed` | Give money | මුදල් දෙන්න | பணம் அளியுங்கள் |
| `Giving.give.openNeedNamed` | Give money toward {need} | {need} සඳහා මුදල් දෙන්න | {need} க்காகப் பணம் அளியுங்கள் |
| `Giving.give.title` | Give to {name} | {name} වෙත පරිත්‍යාග කරන්න | {name} க்கு நன்கொடை அளியுங்கள் |
| `Giving.give.titleNeed` | Give toward {need} | {need} සඳහා පරිත්‍යාග කරන්න | {need} க்காக நன்கொடை அளியுங்கள் |
| `Giving.give.description` | You pay on PayHere's secure page, and the money goes to {name}. Your receipt comes once PayHere confirms it. | ඔබ ගෙවන්නේ PayHere හි ආරක්ෂිත පිටුවෙනි, මුදල {name} වෙත යයි. PayHere තහවුරු කළ පසු ඔබේ රිසිට්පත ලැබේ. | நீங்கள் PayHere இன் பாதுகாப்பான பக்கத்தில் செலுத்துவீர்கள், பணம் {name} க்குச் செல்லும். PayHere உறுதிப்படுத்தியதும் உங்கள் பற்றுச்சீட்டு கிடைக்கும். |
| `Giving.give.signInFirst` | Sign in to give, so your receipt comes to you. You will come back here. | පරිත්‍යාග කිරීමට පුරනය වන්න, එවිට රිසිට්පත ඔබට ලැබේ. ඔබ නැවත මෙහිම පැමිණේවි. | நன்கொடை அளிக்க உள்நுழையுங்கள்; அப்போது பற்றுச்சீட்டு உங்களுக்குக் கிடைக்கும். நீங்கள் இங்கேயே திரும்புவீர்கள். |
| `Giving.give.institutionSession` | You are working in an institution. Gifts are made from your own account. | ඔබ දැන් ආයතනයක වැඩ කරයි. පරිත්‍යාග කරන්නේ ඔබේම ගිණුමෙනි. | நீங்கள் ஒரு நிறுவனத்தில் பணிபுரிகிறீர்கள். நன்கொடைகள் உங்கள் சொந்தக் கணக்கிலிருந்து அளிக்கப்படும். |
| `Giving.give.notAllowed` | This account cannot give online. | මෙම ගිණුමට මාර්ගගතව පරිත්‍යාග කළ නොහැක. | இந்தக் கணக்கால் இணையவழியில் நன்கொடை அளிக்க முடியாது. |
| `Giving.give.amount` | Amount (LKR) | මුදල (රු.) | தொகை (ரூ.) |
| `Giving.give.amountHint` | From LKR 100 to LKR 1,000,000. | රු. 100 සිට රු. 1,000,000 දක්වා. | ரூ. 100 முதல் ரூ. 1,000,000 வரை. |
| `Giving.give.amountInvalid` | Enter an amount from 100 to 1,000,000, with at most two decimals. | 100 සිට 1,000,000 දක්වා මුදලක් ඇතුළත් කරන්න, දශමස්ථාන දෙකකට නොවැඩිව. | 100 முதல் 1,000,000 வரையான தொகையை, அதிகபட்சம் இரண்டு தசம இடங்களுடன் உள்ளிடுங்கள். |
| `Giving.give.detailsHint` | PayHere asks for these. Sethika passes them on and does not keep them. | PayHere මේවා ඉල්ලයි. Sethika ඒවා PayHere වෙත යවයි, තබා නොගනී. | PayHere இவற்றைக் கேட்கிறது. Sethika இவற்றை அனுப்பும், வைத்திருக்காது. |
| `Giving.give.email` | Email | ඊමේල් | மின்னஞ்சல் |
| `Giving.give.emailInvalid` | Enter a valid email address. | වලංගු ඊමේල් ලිපිනයක් ඇතුළත් කරන්න. | சரியான மின்னஞ்சல் முகவரியை உள்ளிடுங்கள். |
| `Giving.give.address` | Address | ලිපිනය | முகவரி |
| `Giving.give.addressInvalid` | Enter your address. | ඔබේ ලිපිනය ඇතුළත් කරන්න. | உங்கள் முகவரியை உள்ளிடுங்கள். |
| `Giving.give.city` | City | නගරය | நகரம் |
| `Giving.give.cityInvalid` | Enter your city. | ඔබේ නගරය ඇතුළත් කරන්න. | உங்கள் நகரத்தை உள்ளிடுங்கள். |
| `Giving.give.cancel` | Not now | දැන් නොවේ | இப்போது வேண்டாம் |
| `Giving.give.save` | Go to PayHere | PayHere වෙත යන්න | PayHere க்குச் செல்லுங்கள் |
| `Giving.return.pageTitle` | Your gift | ඔබේ පරිත්‍යාගය | உங்கள் நன்கொடை |
| `Giving.return.paidTitle` | Thank you | ස්තූතියි | நன்றி |
| `Giving.return.paidBody` | {name} received your gift of {money}. | ඔබේ {money} පරිත්‍යාගය {name} වෙත ලැබුණි. | உங்கள் {money} நன்கொடை {name} க்குக் கிடைத்தது. |
| `Giving.return.receipt` | See receipt {receiptNo} | රිසිට්පත {receiptNo} බලන්න | பற்றுச்சீட்டு {receiptNo} ஐப் பாருங்கள் |
| `Giving.return.mine` | My giving | මගේ පරිත්‍යාග | என் நன்கொடைகள் |
| `Giving.return.waitingTitle` | Waiting for PayHere | PayHere බලාපොරොත්තුවෙන් | PayHere க்காகக் காத்திருக்கிறது |
| `Giving.return.waitingBody` | PayHere has not confirmed this payment yet. Your receipt will come here and to My giving as soon as it does. | PayHere තවම මෙම ගෙවීම තහවුරු කර නැත. තහවුරු කළ වහාම ඔබේ රිසිට්පත මෙහිත්, මගේ පරිත්‍යාග තුළත් දිස් වේ. | PayHere இந்தக் கட்டணத்தை இன்னும் உறுதிப்படுத்தவில்லை. உறுதிப்படுத்தியதும் உங்கள் பற்றுச்சீட்டு இங்கும் என் நன்கொடைகளிலும் தோன்றும். |
| `Giving.return.checkAgain` | Check again | නැවත බලන්න | மீண்டும் பாருங்கள் |
| `Giving.return.failedTitle` | The payment did not go through | ගෙවීම සාර්ථක වූයේ නැත | கட்டணம் நிறைவேறவில்லை |
| `Giving.return.failedBody` | No gift was recorded. You can try again from the page of {name}. | කිසිදු පරිත්‍යාගයක් සටහන් නොවීය. {name} පිටුවෙන් නැවත උත්සාහ කළ හැක. | நன்கொடை எதுவும் பதிவாகவில்லை. {name} பக்கத்திலிருந்து மீண்டும் முயற்சிக்கலாம். |
| `Giving.return.leftTitle` | The payment was not finished | ගෙවීම අවසන් කළේ නැත | கட்டணம் முடிக்கப்படவில்லை |
| `Giving.return.leftBody` | No gift was recorded. You can give again from the page of {name} whenever you wish. | කිසිදු පරිත්‍යාගයක් සටහන් නොවීය. ඔබ කැමති විටෙක {name} පිටුවෙන් නැවත පරිත්‍යාග කළ හැක. | நன்கொடை எதுவும் பதிவாகவில்லை. நீங்கள் விரும்பும்போது {name} பக்கத்திலிருந்து மீண்டும் அளிக்கலாம். |
| `Giving.return.backToPlace` | Back to {name} | {name} වෙත ආපසු | {name} க்குத் திரும்பு |
| `Giving.payments.title` | Online payments | මාර්ගගත ගෙවීම් | இணையவழிக் கட்டணங்கள் |
| `Giving.payments.intro` | Let donors give online through your own PayHere merchant account. The money goes straight to you, never through Sethika. | ඔබේම PayHere වෙළෙන්දා ගිණුම හරහා දායකයන්ට මාර්ගගතව පරිත්‍යාග කිරීමට ඉඩ දෙන්න. මුදල කෙලින්ම ඔබට ලැබේ, Sethika හරහා කිසිවිටෙක නොයයි. | உங்கள் சொந்த PayHere வணிகர் கணக்கின் மூலம் நன்கொடையாளர்கள் இணையவழியில் அளிக்க அனுமதியுங்கள். பணம் நேரடியாக உங்களுக்கு வரும், Sethika வழியாக ஒருபோதும் செல்லாது. |
| `Giving.payments.howTo` | In your PayHere account, open Integrations and add Sethika's web address. When PayHere approves it, which can take a day, copy the merchant ID and the merchant secret here. | ඔබේ PayHere ගිණුමේ Integrations විවෘත කර Sethika හි වෙබ් ලිපිනය එක් කරන්න. PayHere එය අනුමත කළ පසු (දිනක් පමණ ගත විය හැක), Merchant ID සහ Merchant Secret මෙහි පිටපත් කරන්න. | உங்கள் PayHere கணக்கில் Integrations ஐத் திறந்து Sethika இணைய முகவரியைச் சேருங்கள். PayHere அதை அங்கீகரித்ததும் (ஒரு நாள் ஆகலாம்), Merchant ID மற்றும் Merchant Secret ஐ இங்கே நகலெடுங்கள். |
| `Giving.payments.refunds` | Refunds are made in your PayHere portal. Then void the gift in Donations and receipts. | මුදල් ආපසු දීම ඔබේ PayHere ද්වාරයෙන් කරන්න. ඉන්පසු පරිත්‍යාග සහ රිසිට්පත් තුළ එම පරිත්‍යාගය අවලංගු කරන්න. | பணத்தைத் திரும்பக் கொடுப்பது உங்கள் PayHere தளத்தில் செய்யப்படும். பின்னர் நன்கொடைகள் மற்றும் பற்றுச்சீட்டுகளில் அந்த நன்கொடையை ரத்து செய்யுங்கள். |
| `Giving.payments.merchantId` | Merchant ID | Merchant ID | Merchant ID |
| `Giving.payments.merchantIdInvalid` | Enter the merchant ID from PayHere: letters and digits only. | PayHere හි Merchant ID ඇතුළත් කරන්න: අකුරු සහ ඉලක්කම් පමණි. | PayHere இன் Merchant ID ஐ உள்ளிடுங்கள்: எழுத்துகளும் எண்களும் மட்டும். |
| `Giving.payments.secret` | Merchant secret | Merchant Secret | Merchant Secret |
| `Giving.payments.secretNew` | Stored encrypted. Nobody can read it back, not even you. | සංකේතනය කර ගබඩා වේ. එය නැවත කියවීමට කිසිවෙකුට, ඔබටවත්, නොහැක. | குறியாக்கம் செய்து சேமிக்கப்படும். அதை யாராலும், உங்களாலும்கூட, மீண்டும் படிக்க முடியாது. |
| `Giving.payments.secretKeep` | Saved and encrypted. Leave this empty to keep it. A new merchant ID needs its own secret. | සංකේතනය කර සුරැකී ඇත. එය එලෙසම තබා ගැනීමට මෙය හිස්ව තබන්න. නව Merchant ID එකකට එහිම Secret එකක් අවශ්‍යයි. | சேமிக்கப்பட்டு குறியாக்கம் செய்யப்பட்டுள்ளது. அதை வைத்திருக்க இதை வெறுமையாக விடுங்கள். புதிய Merchant ID க்கு அதற்கான Secret தேவை. |
| `Giving.payments.secretInvalid` | Enter the merchant secret from PayHere. | PayHere හි Merchant Secret ඇතුළත් කරන්න. | PayHere இன் Merchant Secret ஐ உள்ளிடுங்கள். |
| `Giving.payments.enabled` | Donors can give online | දායකයන්ට මාර්ගගතව පරිත්‍යාග කළ හැක | நன்கொடையாளர்கள் இணையவழியில் அளிக்கலாம் |
| `Giving.payments.enabledHint` | When this is off, your page does not offer it. | මෙය අක්‍රිය විට, ඔබේ පිටුවේ එය නොපෙන්වයි. | இது முடக்கப்பட்டிருக்கும்போது, உங்கள் பக்கத்தில் இது காட்டப்படாது. |
| `Giving.payments.savedBy` | Last saved on {date} by {name}. | අවසන් වරට {date} දින {name} විසින් සුරකින ලදී. | கடைசியாக {date} அன்று {name} சேமித்தார். |
| `Giving.payments.save` | Save | සුරකින්න | சேமி |
| `Giving.payments.saved` | The online payment settings are saved. | මාර්ගගත ගෙවීම් සැකසුම් සුරකින ලදී. | இணையவழிக் கட்டண அமைப்புகள் சேமிக்கப்பட்டன. |
| `Giving.payments.stale` | Someone saved these settings while you were looking at them. Open the page again. | ඔබ බලා සිටියදී කෙනෙකු මෙම සැකසුම් සුරැකුවා. පිටුව නැවත විවෘත කරන්න. | நீங்கள் பார்த்துக்கொண்டிருக்கும்போது யாரோ இந்த அமைப்புகளைச் சேமித்தனர். பக்கத்தை மீண்டும் திறங்கள். |
| `Giving.payments.readOnly` | Only someone who may change the merchant can save these settings. | මෙම සැකසුම් සුරැකිය හැක්කේ වෙළෙන්දා වෙනස් කිරීමට අවසර ඇති අයට පමණි. | வணிகரை மாற்ற அனுமதி உள்ளவர் மட்டுமே இந்த அமைப்புகளைச் சேமிக்க முடியும். |
| `Giving.payments.retry` | Try again | නැවත උත්සාහ කරන්න | மீண்டும் முயற்சிக்கவும் |
| `Giving.errors.online_giving_off` | This place is not taking online gifts right now. | මෙම ස්ථානය දැනට මාර්ගගත පරිත්‍යාග භාර ගන්නේ නැත. | இந்த இடம் தற்போது இணையவழி நன்கொடைகளை ஏற்கவில்லை. |
| `Giving.errors.payment_not_found` | There is no such payment on this account. | මෙම ගිණුමේ එවැනි ගෙවීමක් නැත. | இந்தக் கணக்கில் அப்படி ஒரு கட்டணம் இல்லை. |
| `Giving.errors.phone_required` | Your account needs a confirmed phone number before you can give. | පරිත්‍යාග කිරීමට පෙර ඔබේ ගිණුමට තහවුරු කළ දුරකථන අංකයක් අවශ්‍යයි. | நன்கொடை அளிக்கும் முன் உங்கள் கணக்கில் உறுதிப்படுத்தப்பட்ட தொலைபேசி எண் தேவை. |
| `Shell.items.payments` | Online payments | මාර්ගගත ගෙවීම් | இணையவழிக் கட்டணங்கள் |
