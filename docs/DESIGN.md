# Sethika design language

Sethika should not look like every other dashboard, and it should not look busy either. The
rule is **four signatures, used with restraint, on a calm base**. Anything not listed here
stays plain. The tokens live in `web/app/globals.css`, the primitives in `web/components/ui`,
and every state can be seen at `/design`, `/si/design` and `/ta/design`.

## The base: warm and calm

- Light: **Rice Ivory** canvas, **Deep Ink** text, **Estate Green** for actions, **Offering Gold**
  sparingly. Dark: the same roles on a near-black canvas (see below).
- No flat grey: neutrals (`muted`, `line`, `hairline`, `subtle`) are tinted toward the green; in
  dark only faintly, so it reads as near-black.
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
- Dark is built on a **near-black** canvas (`#121514`, chosen 2026-11-15 over Deep Ink, like the
  dashboard the restyle follows), neutral with a faint green cast, so the brand colours are the
  only colour on it. The greens lighten (the primary becomes a soft mint with dark text) so they
  still read on it. Gold stays gold.
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
3. **Warm depth.** Elevation is a green-tinted shadow (`shadow-lift`), never grey. One level
   only, for cards and toasts; sheets have their own.
4. **Two voices.** Outfit does the work: every label, button and number. Fraunces italic is
   the human voice: the tagline, a brand eyebrow, later the gratitude wall and empty states.
   Never Fraunces on a control.

**The rim mark.** A short 3px line on the top edge, the bowl's rim. Gold on the one card that
matters most on a screen; in the status colour on a toast. Never more than one gold rim per view
(the menu's pill carries its own, on its left edge, and travels with it).

## The app shell (plan 04, 2026-10-25; restyled 2026-11-15)

Every screen that is not a sign-in form sits in one shell (`components/shell/AppShell.tsx`).
The 2026-11-15 restyle takes the layout and motion of a smart-home dashboard the user liked
(labelled sidebar, layered panels, a pill that glides) and keeps Sethika's own colours, the bowl
and the "no glass" rule.

- **Panels on a backdrop** (signed in, `lg` and up): the window is the `backdrop` colour, a step
  deeper than the canvas; the sidebar and the page are two canvas panels laid on it, with large
  rounded corners, a hairline and `shadow-lift`. Cards sit on the canvas panel as before, so every
  measured contrast still holds. On phones there are no panels: the page is the canvas.
- **Top bar**: who the session acts as (the context chip, which also switches between the
  person's own account and their institutions), language, account menu; the brand too on phones,
  where there is no sidebar. Signed out: the brand and the public links.
- **The sidebar** (`Sidebar.tsx`, `SidebarNav.tsx`): the brand, then the menu as labelled links,
  the dashboard first and each section's screens under its name in small capitals. The modules of
  the catalogue are not shown as a level of their own: there are few screens, and a flat list is
  read at a glance. Below `lg` the same list is in a sheet from the left.
- **The glide pill** marks the page the user is on: a filled `primary` pill with ivory text and
  the gold rim on its left edge. It is a copy of the list in the active colours, clipped to a band,
  so a label turns ivory exactly where the pill is under it (see Motion).
- **Full width.** The page uses the whole width beside the sidebar; lists add columns as the screen
  grows (`lg:grid-cols-2 2xl:grid-cols-3`), forms and paragraphs keep a readable width of their
  own (`max-w-3xl`, `max-w-prose`).
- **Brand panels may carry a tonal gradient of their own green** (the dashboard's greeting, the
  landing page's picture): Estate Green from a lighter to a darker shade, never a
  second hue, never glass. This relaxes "no gradients" for these two places only.
- The menu is the RBAC catalogue's shape filtered by `.VIEW` permissions (`lib/shell/nav.ts`);
  hiding a link is UX only.
- **Toasts sit top right, under the bar.** At the bottom they covered the save bars and primary
  buttons placed where the thumb reaches (found by the access e2e test on a 320 px phone).

## Motion

Short (under 300 ms), ease-out, and only where it explains something: a sheet arriving, a
toast settling, a button press settling (`scale 0.97`), a wrong code shaking its head once.
Every animation turns off under `prefers-reduced-motion`; a spinner keeps turning, slowly.

Two exceptions, both in the shell, both explaining where the user went:

- **The glide** (`components/shell/useGlide.ts`): when the page changes, the menu's pill moves to
  the new link. Its leading edge leaves first (360 ms, quick out), its trailing edge follows and
  catches up (550 ms, in-out), so for a moment the pill stretches across the gap. No animation
  library: the two edges are registered custom properties (`--glide-start`, `--glide-end`,
  `globals.css`) that the browser transitions one at a time. Pages in other route groups mount a
  new shell, so the pill remembers where it was and sets off from there.
- **Page enter**: the new page rises 10 px and fades in (320 ms) when the path changes, never on
  the first load (`PageEnter` in `AppShell.tsx`, Web Animations, so no layout remounts).

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
