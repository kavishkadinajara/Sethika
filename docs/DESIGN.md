# Sethika design language

Sethika should not look like every other dashboard, and it should not look busy either. The
rule is **four signatures, used with restraint, on a calm base**. Anything not listed here
stays plain. The tokens live in `web/app/globals.css`, the primitives in `web/components/ui`,
and every state can be seen at `/design`, `/si/design` and `/ta/design`.

## The base: warm and calm

- **Rice Ivory** canvas, **Deep Ink** text, **Estate Green** for actions, **Offering Gold** sparingly.
- No grey anywhere: neutrals (`muted`, `line`, `hairline`, `subtle`) are tinted toward the green.
- No gradients, no glass, no stock photos, no decorative illustrations in the UI chrome.
- Faith-neutral: no religion's symbols, colours or vocabulary in shared UI (CLAUDE.md).

## The four signatures

1. **The bowl.** Sethika's mark is a bowl, so things that *hold* or *receive* are shaped like
   one: gentle top corners, deeper bottom corners.
   - `rounded-bowl`: cards, toasts.
   - `rounded-cup`: inputs and OTP cells, with a faint inner shadow (`shadow-well`) that
     says "put something in here".
   - Buttons stay full pills. Do not bowl a button.
2. **The offering glow.** One focus style everywhere (`focusRing` in `lib/utils.ts`): an
   Estate Green outline carries the contrast, a soft gold halo carries the brand. Never
   remove it and never invent another.
3. **Warm depth.** Elevation is a green-tinted shadow (`shadow-lift`), never grey. One level
   only, for cards and toasts; sheets have their own.
4. **Two voices.** Outfit does the work: every label, button and number. Fraunces italic is
   the human voice: the tagline, a brand eyebrow, later the gratitude wall and empty states.
   Never Fraunces on a control.

**The rim mark.** A short 3px line on the top edge, the bowl's rim. Gold on the one card that
matters most on a screen; in the status colour on a toast. Never more than one gold rim per view.

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

Known cost outside this budget: the root layout fetches effective permissions on every
request (`lib/permissions/server.ts`), which delays the first byte by the API's response
time. That module is due for rework and is left alone here.
