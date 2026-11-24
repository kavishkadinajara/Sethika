# Sethika design language

Sethika should not look like every other dashboard, and it should not look busy either. The
rule is **four signatures, used with restraint, on a calm base**. Anything not listed here
stays plain. The tokens live in `web/app/globals.css`, the primitives in `web/components/ui`,
and every state can be seen at `/design`, `/si/design` and `/ta/design`.

## The base: warm and calm

- Light: **Rice Ivory** canvas, **Deep Ink** text, **Estate Green** for actions, **Offering Gold**
  sparingly. Dark: a **true black** canvas, ivory text, **Offering Gold** for actions (see below).
- No cool grey anywhere: neutrals (`muted`, `line`, `hairline`, `subtle`) are warm, tinted toward
  the green in light and toward ivory in dark.
- No gradients, no glass, no stock photos, no decorative illustrations in the UI chrome.
- Faith-neutral: no religion's symbols, colours or vocabulary in shared UI (CLAUDE.md).

## Light and dark

Sethika follows the device's light or dark setting by default. The `ThemeSwitcher`
(`components/ui/theme-switcher.tsx`) offers Auto / Light / Dark; the choice is a
`sethika_theme` cookie, applied before the first paint by an inline script in `<head>`
(`components/theme/`), so a saved theme never flashes the other one.

- **Components use semantic colours only**: `canvas`, `surface`, `subtle`, `hairline`, `line`,
  `fg`, `muted`, `brand`, `primary`, `on-primary`, `accent`, `on-accent`, `danger`, `success`.
  Each has a light and a dark value in `globals.css`, with measured contrast in its header.
- The brand colours (`estate`, `gold`, `ink`, `ivory`) are fixed and are for places where the
  brand itself is the point, such as a panel that is Estate Green in both themes.
- Dark is **true black** (plan 05, R1; the user, 2026-10-31: "no green, full black"). Canvas
  `#000`, surfaces `#0C0C0C` and `#161616`, ivory text. **Offering Gold is the primary and the
  brand colour** (filled actions, links, headings, the focus outline), with near-black text on it.
  Green appears only as the success status. Depth is a faint light top edge plus a black shadow; a
  green-tinted shadow reads as mud on black. A brand panel that is Estate Green in light becomes a
  surface with a hairline in dark, never a green field on the black.
- Measured on the dark canvas: text 18.6:1, muted 7.9, gold 9.5, input borders 3.7 (3.4 on a
  surface), near-black on gold 8.9. The full table is in the header of `globals.css`.
- Avoid `dark:`. If a token cannot express a difference, `dark:` works and honours the switcher
  (it is redefined in `globals.css`), but add a token first.

## The four signatures

1. **The bowl.** Sethika's mark is a bowl, so things that *hold* or *receive* are shaped like
   one: gentle top corners, deeper bottom corners.
   - `rounded-bowl`: cards, toasts.
   - `rounded-cup`: inputs and OTP cells, with a faint inner shadow (`shadow-well`) that
     says "put something in here".
   - Buttons stay full pills. Do not bowl a button.
   - On a calendar the bowl is a gauge (`components/bookings/Bowl.tsx`): an empty bowl is a day
     waiting for a sponsor, and it fills as sponsors book it, until it is full. The words beside it
     (or its cell's label) always say the same thing, so it is never the only signal.
2. **The offering glow.** One focus style everywhere (`focusRing` in `lib/utils.ts`): an
   Estate Green outline carries the contrast, a soft gold halo carries the brand. Never
   remove it and never invent another.
3. **Warm depth.** Elevation is a green-tinted shadow (`shadow-lift`) in light, never grey; in
   dark, a faint light top edge and a black shadow. One level only, for cards and toasts; sheets
   have their own.
4. **Two voices.** Outfit does the work: every label, button and number. Fraunces italic is
   the human voice: the tagline, a brand eyebrow, later the gratitude wall and empty states.
   Never Fraunces on a control.

**The rim mark.** A short 3px line on the top edge, the bowl's rim. Gold on the one card that
matters most on a screen; in the status colour on a toast. Never more than one gold rim per view.

## The app shell (plan 04, 2026-10-25; the living sidebar, plan 05, 2026-10-31)

Every screen that is not a sign-in form sits in one shell (`components/shell/AppShell.tsx`):

- **Top bar**: brand, language, account menu; on phones, the menu button. Signed out: the public
  links instead.
- **The living sidebar** (`Sidebar.tsx`, `lg` and up, 17 rem): from the top, the **institution
  card** (initials in a bowl-shaped tile with the gold rim, the name; it is the context switcher),
  **Jump to** (`Ctrl K` / `⌘K`, a combobox over the screens the person may view), **the menu**
  (every link visible under its module's name; the link the user is on carries the rim on its
  left edge), and **today's table** for those who read the calendar (each of today's meals as a
  bowl that fills as sponsors take its places). A link may carry a **gold count badge**: requests
  to answer, registrations waiting, invitations; zero shows nothing. The counts and the table are
  read every minute while the tab is visible, on focus, and right after a decision on the screen.
- **It folds** to a column of icons (kept per browser); folded, each control's name shows beside it
  on hover and focus. Tooltips and menus are portalled, so nothing of the sidebar is trapped in its
  own stacking context or scroll box. Below `lg` the same body opens in a sheet from the left.
- **During a switch** the menu of the old context stays (dimmed, `aria-busy`) until the new one's
  permissions arrive: the sidebar never blinks out.
- **Full width.** The page uses the whole width beside the sidebar; lists add columns as the screen
  grows (`lg:grid-cols-2 2xl:grid-cols-3`), forms and paragraphs keep a readable width of their
  own (`max-w-3xl`, `max-w-prose`).
- **Brand panels may carry a tonal gradient of their own green** in light (the landing page's
  picture): Estate Green from a lighter to a darker shade, never a second hue, never glass. In
  dark they are plain surfaces (R1). The plan 04 rail and its panel are gone, and so is the
  dashboard's greeting panel.
- The menu is the RBAC catalogue's shape filtered by `.VIEW` permissions (`lib/shell/nav.ts`);
  hiding a link is UX only, and so is hiding a count: the API sends only the counts the caller's
  permissions allow.
- **Toasts sit top right, under the bar.** At the bottom they covered the save bars and primary
  buttons placed where the thumb reaches (found by the access e2e test on a 320 px phone).

## The dashboard: the Day Ledger (plan 05, 2026-10-31)

The signed-in home page reads like a ledger of the day, one for each kind of session
(`components/home/*Day.tsx`):

- **The frame.** A large date in Fraunces italic (the weekday over the day and month), and beside
  it **one sentence that says what today needs**, composed from data by `lib/dashboard/sentence.ts`:
  requests to answer first, then the next meal and the places still open; for a donor, their next
  meal to give; for the platform, the review queue and its oldest. Never decoration: a number the
  session may not see is never in it, and while its data loads it is a quiet placeholder.
- **The panels** are bowl cards, each behind the permission of the screen it summarises; a panel
  the session may not see is not drawn and its data is not asked for. Each title is a heading and
  each panel a named region; a link at the right goes to the full screen.
- **Today's table as a timeline**, 05:00 to 21:00: a bowl at each meal's time, as full as its
  places, a gold dot where a request waits, a gold **now** needle. The line is a picture
  (`aria-hidden`); the list under it says the same in words, in the sidebar's own phrases.
- **The month, bowl by bowl**: a small grid of bowls, today ringed in gold, each day a link to that
  day on the calendar.
- **Ledger lines**: a time (or a short date, if not today) in a narrow tabular column, then a
  sentence, then the number. No status chip: the sentence names the change, and a chip would read
  as the booking's status now. Lists, not tables: they reflow on a phone.
- A decision taken on the dashboard is the calendar's own control (`BookingItem`); afterwards the
  sentence, the panels and the sidebar's counts all move together.

## Motion

Short (under 300 ms), ease-out, and only where it explains something: a sheet arriving, a
toast settling, a button press settling (`scale 0.97`), a wrong code shaking its head once.
Every animation turns off under `prefers-reduced-motion`; a spinner keeps turning, slowly.

## Usability rules (not negotiable)

- Every control is at least 44 × 44 px. Heights are minimums, never fixed, and labels wrap:
  Sinhala and Tamil run longer than English and must never clip.
- Text is 16 px or more in controls (also stops iOS zooming into inputs).
- Contrast: text ≥ 4.5:1, control boundaries ≥ 3:1. Gold is never text and never a
  boundary on ivory (2.0:1). Measured ratios are in the header of `globals.css`.
- Labels are tied to their control, errors are announced (`Field`) and say what to do next.
- One primary button per screen. On phones, primary actions sit where the thumb reaches.
- A loading button keeps its width (the label fades and a spinner takes its place).

## Performance budget

The UI must feel instant on a mid-range Android phone over 4G. Measured with Chromium at
4× CPU throttling and 9 Mbps / 60 ms RTT (`/login` placeholder, 2026-10-01):

| | Budget | Now |
|---|---|---|
| Fonts on an English page | ≤ 60 KB, two files | 54 KB (Outfit, Fraunces 400 italic) |
| Fonts on a Sinhala / Tamil page | Latin + one Noto script only | 212 KB / 104 KB |
| JavaScript before interaction (gzip, modern browsers) | ≤ 160 KB | ~147 KB, of which ~116 KB is React + Next |
| CSS (gzip) | ≤ 15 KB | 8 KB |
| Layout shift (CLS) | < 0.1 | 0 (en), ≤ 0.05 (si/ta) |

How the budget is kept:

- **No icon, animation or toast library.** Icons are inline SVG (`components/ui/icons.tsx`);
  animations are CSS keyframes in `globals.css`.
- **Anything a page may never use loads late.** Radix Toast is fetched when the browser is
  idle or at the first toast (`toast.tsx`), never on the critical path.
- **Fonts load only for the script on screen.** Noto Sinhala and Noto Tamil are not
  preloaded; `unicode-range` means a page fetches them only when it shows that script. The
  language switcher draws other scripts' names in the platform font (`font-system`) for the
  same reason.
- **Client components get only their own messages.** The root provider ships the shared
  namespaces (`i18n/pick.ts`); a page whose client components need more wraps itself in
  its own provider.
- **No images in UI chrome.** Content images (institutions, gratitude wall) come later
  through `next/image`, sized and lazy.

- **Pages are prerendered.** Nothing in the root layout depends on the request: the session
  and the permissions load in the browser (`lib/auth`, `PermissionProvider`), so every locale's
  pages are static HTML and the first byte never waits for the API.
