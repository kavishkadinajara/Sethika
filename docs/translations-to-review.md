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
