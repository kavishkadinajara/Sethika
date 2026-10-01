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
| `NotFound.title` | Page not found | පිටුව හමු නොවීය | பக்கம் கிடைக்கவில்லை | | |
| `NotFound.home` | Go to the home page | මුල් පිටුවට යන්න | முகப்புப் பக்கத்திற்குச் செல்லவும் | | |
| `Common.close` | Close | වසන්න | மூடு | | |
| `Common.loading` | Loading | පූරණය වෙමින් | ஏற்றப்படுகிறது | | |
| `Common.required` | required | අනිවාර්යයි | கட்டாயம் | | |
| `Toast.region` | Notifications ({hotkey}) | දැනුම්දීම් ({hotkey}) | அறிவிப்புகள் ({hotkey}) | | |
| `Toast.item` | Notification | දැනුම්දීම | அறிவிப்பு | | |
| `LanguageSwitcher.label` | Language | භාෂාව | மொழி | | |
| `LanguageSwitcher.names.en` | English | English | English | | |
| `LanguageSwitcher.names.si` | සිංහල | සිංහල | සිංහල | | |
| `LanguageSwitcher.names.ta` | தமிழ் | தமிழ் | தமிழ் | | |
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
