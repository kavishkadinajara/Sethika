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
5. **Developer-facing strings** (`Home.*`, `PermissionDebug.*`) belong to the permission debug page
   and will be replaced; review them last.

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
