# Plan 05: Brand-new look: true-black dark, the living sidebar, the day-ledger dashboard

Branch: `redesign`, cut from `develop` after `access-hardening` merges.

Status: **X1 (true-black dark) done; X2 (living sidebar) built and tested, not committed; booking-number jump moved to X3; X3-X4 not started.** Last updated 2026-10-31. Update this line and the §4 boxes
whenever either changes; a fresh session trusts them.

Why this plan exists: on 2026-10-31 the user judged the plan 04 shell and dashboard generic ("the
same thing is everywhere") and asked for a creative, brand-new design: dark mode in true black with
no green canvas, a new sidebar, and a unique dashboard. Three dashboard and three sidebar concepts
were drawn on a design canvas (https://claude.ai/artifact/U6rZRjBtCNrUnwQuVgmunh); the user took
**A, Day Ledger** and **S2, Living sidebar**. The plan 04 tooltip z-index bug was fixed on
`access-hardening` (rail `z-20`) and goes away with the rail.

The rules of plans 01-04 still hold: every endpoint carries `[HasPermission]` (or is on the
anonymous/authenticated list in `EndpointPolicyTests`), queries are Dapper with parameterised SQL,
anything the database enforces is tested against real SQL Server and Redis, every test is shown to
fail under a mutation, and every screen is tested with the `browser-test` skill (desktop + 320 px,
en/si/ta, light and dark).

---

## 1. Decisions

### R1: Dark mode is true black, gold carries the brand

The dark theme drops the Deep Ink green canvas. Canvas `#000000`, surface `#0C0C0C`, subtle
`#161616`, hairline `#242424`, line `#6B665C`, text `#F4F1E8`, muted `#A39E92`. In dark the
**primary is Offering Gold** (`#E0A33E`, text on it `#0A0A0A`); green appears only as a status
(`success #86D6AE`). Neutrals are warm (toward ivory), never cool grey. Measured on `#000`: fg 18.6,
muted 7.9, gold 9.5, success 12.2, danger 10.3, line 3.7 (boundary ≥ 3); `#0A0A0A` on gold 8.9.
Light mode keeps Rice Ivory and Estate Green. The rail's green gradient panel goes with the rail
(W4's brand panels are retired); the dashboard has no brand panel either.

### R2: The living sidebar replaces the rail and its flyout

A full-height sidebar (`lg` and up, 17 rem) holds, top to bottom:

1. **Who you act as**: the institution card (initials in a bowl-shaped tile, name, your role, a gold
   rim); it is the context switcher, moved down from the top bar.
2. **Jump to** (`Ctrl K` / `⌘K`): a command palette over the screens you may view (nav tree,
   already filtered) and, for staff, a booking number (`BK-…`) or appointment number (`APT-…`) typed
   whole. No fuzzy search of people in this plan.
3. **The menu**: the same tree as W2 (portal → module → screen, filtered by `.VIEW`), flattened to
   headed groups, every link visible (no accordion, no flyout). A link may carry a **count badge**.
4. **Today's table** (institution sessions holding `BOOKINGS.CALENDAR.VIEW`): today's meal slots as
   bowls (full, partly full, empty), each a link to that day on the calendar.

It folds to an icon column (choice kept per browser in `localStorage`, wrapped in try/catch). Below
`lg` the same sidebar opens as a left sheet from the top bar's menu button. The top bar keeps the
brand, language, theme and account menu.

### R3: Counts come from one summary endpoint, shaped by permission

`GET /api/me/summary` (authenticated; on the authenticated-only list) returns only the numbers the
caller may see, each guarded by the same permission as the screen it counts:
`bookingsToAnswer` (`BOOKINGS.CALENDAR.APPROVE`), `registrationsWaiting` (`ONBOARDING.REQUESTS.VIEW`),
`invitations` (own), later `appointmentsToAssign` (Phase E). One Dapper query, permission checks
read from the request's permission set (never re-derived), cached nowhere: it is cheap and must
be current. The sidebar refetches it on focus and every 60 s; SignalR replaces the poll in Phase C.

### R4: The dashboard is the Day Ledger, one per kind of session

Shared frame: a large italic date (Fraunces) beside **one sentence that says what today needs**,
composed from data, never decoration. Then role panels, all bowl-radius cards on the R1 surfaces:

- **Institution staff** (by permission): *Today's table* timeline (05:00-21:00, a bowl per meal
  slot at its time, a NOW needle in Colombo time); *Waiting for you* (bookings to answer, approve and
  decline inline, the same endpoints as the calendar); *Month, bowl by bowl* (each day's fill);
  *The ledger* (the last 10 booking events of the institution). Services panel after Phase E 8.7.
- **Donor**: the sentence names their next meal to give; *Your table* (upcoming bookings as a
  ledger); *Places you give to* (favourites/recent), *Find a place* link.
- **Platform**: registrations waiting (oldest first, with age), institutions by status, the ledger
  of recent approvals.

Each panel's data is one Dapper query behind an endpoint guarded by the panel's screen permission;
a panel the caller may not see is not rendered and its query is never sent.

### R5: The signatures stay, the base changes

DESIGN.md's four signatures (bowl, offering glow, warm depth, two voices) and the rim mark stay.
"Warm depth" in dark is a 1 px light top edge plus a black shadow (a green shadow on black reads as
mud). DESIGN.md is rewritten for R1-R4 in the same branch.

---

## 2. Data

- `bookingsToAnswer`: `booking.Bookings` of the session's institution with status `Requested` and
  a date today or later (RLS also scopes it).
- Today's table and the month: `booking.SlotInstances` + `booking.MealSlots` for the institution,
  Colombo dates (reuse the calendar's month query; no second copy of its date logic).
- The ledger: booking events of the institution, top 10 by time: each booking's `CreatedAt`
  (requested), `DecidedAt` (approved or rejected), `CancelledAt`, `CompletedAt` (served), unpivoted
  in SQL (`CROSS APPLY (VALUES …)`), with `BookingNo`, the meal and the date; donor names as the
  calendar shows them. No new table.
- Donor panels: `booking.Bookings` of the user; `auth.FavoriteInstitutions`.
- Platform: `org.RegistrationRequests` by status and age; `org.Institutions` by status.

## 3. Out of scope

Fuzzy people search, customisable dashboards, charts beyond the bowls, SignalR (Phase C).

## 4. Work breakdown

### X1: Tokens and dark
- [x] `globals.css` dark tokens (R1) in both the `[data-theme=dark]` block and the media block; light unchanged
- [x] Contrast table in the header of `globals.css`; `/design`, landing, login, institutions seen in both themes, desktop + 320 px
- [x] Hard-coded greens moved to tokens (avatars, chips, mobile nav, placeholder, browser chrome colour); the landing panel is a surface in dark
- [ ] DESIGN.md rewritten for R1-R5 (R1, warm depth and the sidebar done; the dashboard section with X3)

### X2: Living sidebar
- [x] The P1 wobble: during a switch the old menu stays (busy) until the new permissions arrive; at a session's start no half menu
- [x] `Sidebar` (sections, groups, links, badges, fold), mobile sheet reuses it; rail, flyout and `NavGroups` removed
- [x] Institution card as the context switcher (`ContextCard`, from `ContextChip`); top bar slimmed
- [x] Jump-to palette (`Ctrl K`): screens. A whole booking/appointment number needs a lookup endpoint and a calendar that opens on one booking: moved to X3, with the dashboard's inline decisions
- [x] `GET /api/me/summary` + tests (each count shown only with its permission; a count from another institution never leaks; mutations: 5/6 killed, the institution filter's mutant survives because RLS scopes the count too, kept as defence in depth)
- [x] Today's table in the sidebar: `GET /api/institution/today` (`BOOKINGS.CALENDAR.VIEW`), every slot laid today by `MealSlotRules.Serves` (the engine's own weekday and season rule), blocked ones shown closed; integration tests + 8/8 mutants killed. X3's timeline reads the same endpoint
- [x] e2e: menu per role (shell.spec rewritten), fold (tooltips on top, kept per browser), palette, badges (and their refresh after a decision), today's table, the P1 switch never emptying the menu, 320 px sheet, light + dark: 22/22

### X3: Day Ledger dashboard
- [ ] Endpoints + Dapper queries for the institution panels, the donor panels, the platform panels; integration tests + mutations
- [ ] The sentence composer (unit-tested in `lib/`, en/si/ta)
- [ ] Institution, donor and platform dashboards; inline approve/decline reuse the calendar's calls
- [ ] e2e per role, desktop + 320 px, en/si/ta, light + dark; screenshots read

### X4: Every other screen on the new base
- [ ] Walk every page in both themes; fix anything that assumed the green dark canvas
- [ ] Translations of the new strings into `docs/translations-to-review.md`
