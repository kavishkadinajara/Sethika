# Plan 05: Brand-new look: true-black dark, the living sidebar, the day-ledger dashboard

Branch: `redesign`, cut from `develop` after `access-hardening` merges.

Status: **X1, X2 and X3 done (X3 in 6d7da7d + 7131448); X4 built and tested (admin lists in c8933f5, the rest uncommitted).** Last updated 2026-11-01. Update this line and the §4 boxes
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
- The ledger: booking events of the institution, top 10 by time, with `BookingNo`, the meal and
  the date; donor names as the calendar shows them. No new table. **Built differently (X3):** it
  reads `booking.BookingStatusHistory`, which the `Booking` aggregate writes on every transition,
  the making included, instead of unpivoting the booking's own timestamps. A booking approved and
  later cancelled then shows both changes; the timestamps hold only the latest of each kind.
- Donor panels: `booking.Bookings` of the user (`GET /api/bookings/mine`); "Places you give to"
  comes from the institutions of those bookings, not `auth.FavoriteInstitutions`, which no screen
  fills yet.
- Platform: `org.RegistrationRequests` by status and age (the queue's own `GET
  /api/admin/registrations`, oldest first); `org.Institutions` by status; the latest decisions.

## 3. Out of scope

Fuzzy people search, customisable dashboards, charts beyond the bowls, SignalR (Phase C).

## 4. Work breakdown

### X1: Tokens and dark
- [x] `globals.css` dark tokens (R1) in both the `[data-theme=dark]` block and the media block; light unchanged
- [x] Contrast table in the header of `globals.css`; `/design`, landing, login, institutions seen in both themes, desktop + 320 px
- [x] Hard-coded greens moved to tokens (avatars, chips, mobile nav, placeholder, browser chrome colour); the landing panel is a surface in dark
- [x] DESIGN.md rewritten for R1-R5 (R1, warm depth, the sidebar, and the Day Ledger's section with X3)

### X2: Living sidebar
- [x] The P1 wobble: during a switch the old menu stays (busy) until the new permissions arrive; at a session's start no half menu
- [x] `Sidebar` (sections, groups, links, badges, fold), mobile sheet reuses it; rail, flyout and `NavGroups` removed
- [x] Institution card as the context switcher (`ContextCard`, from `ContextChip`); top bar slimmed
- [x] Jump-to palette (`Ctrl K`): screens. A whole booking/appointment number needs a lookup endpoint and a calendar that opens on one booking: moved to X3, with the dashboard's inline decisions
- [x] `GET /api/me/summary` + tests (each count shown only with its permission; a count from another institution never leaks; mutations: 5/6 killed, the institution filter's mutant survives because RLS scopes the count too, kept as defence in depth)
- [x] Today's table in the sidebar: `GET /api/institution/today` (`BOOKINGS.CALENDAR.VIEW`), every slot laid today by `MealSlotRules.Serves` (the engine's own weekday and season rule), blocked ones shown closed; integration tests + 8/8 mutants killed. X3's timeline reads the same endpoint
- [x] e2e: menu per role (shell.spec rewritten), fold (tooltips on top, kept per browser), palette, badges (and their refresh after a decision), today's table, the P1 switch never emptying the menu, 320 px sheet, light + dark: 22/22

### X3: Day Ledger dashboard
- [x] Endpoints + Dapper queries. New: `GET /api/institution/ledger` and `GET /api/institution/bookings/{bookingNo}`
      (both `BOOKINGS.CALENDAR.VIEW`), `GET /api/admin/institutions/statuses` (`PLATFORM.INSTITUTIONS.VIEW`),
      `GET /api/admin/registrations/decided` (`ONBOARDING.REQUESTS.VIEW`). Reused: `/api/institution/today`,
      the calendar's `/api/institution/bookings?from&to&status`, `/api/bookings/mine`, the review queue.
      Integration tests (5 new) + mutations: ledger and jump 5/8 killed (the two institution filters
      survive because RLS scopes the rows too, kept as defence in depth like `/api/me/summary`'s;
      upper-casing the number survived because the collation ignores case, so it was removed);
      platform 8/8 killed (one only after the test learned that an open request is no decision)
- [x] The sentence composer (`lib/dashboard/sentence.ts`, 16 unit tests; en/si/ta messages)
- [x] Institution, donor and platform dashboards (`components/home/*Day.tsx`); inline accept/decline is
      the calendar's own `BookingItem`, now exported; a decision marks the shell stale and the dashboard
      reloads with the sidebar. The quick-action grid and the greeting went: the sidebar is the way
      to every screen, and the dashboard says what today needs
- [x] Jump-to: a whole `BK-…` number (any case) opens `/bookings/calendar?date=…&booking=…`, the booking
      marked (`aria-current`) and scrolled into view. `APT-…` waits for Phase E 8.7's appointments agenda
- [x] e2e per role, desktop + 320 px, en/si/ta, light + dark; screenshots read: `e2e/shell/dashboard.spec.ts`
      (owner's day with an inline accept, the palette's jump, donor, platform), access/shell/review updated:
      shell + dashboard 29/29, access + review green. Found by them: a long institution name widened the
      donor's page at 320 px (single-column lists need `grid-cols-1`, an implicit `auto` track grows to
      the text); Sinhala's vowel signs ran into the line above the large date at `leading-[0.95]`; an
      address alone cannot show a signed-in page in another language (A11 moves it back), so the test
      switches through the language menu. New helper `widerThanWindow` names what scrolls sideways

### X4: Every other screen on the new base
- [x] Walk every page in both themes: `e2e/shell/walk.spec.ts` opens every page of each kind of
      session (signed out, donor, owner, platform) in dark, desktop + 320 px; none errs or scrolls
      sideways, and the pictures were read (light is covered by every other spec)
- [x] Admin lists (`/admin/users`, `/admin/institutions`, asked by the user 2026-10-31). Server-side
      pages (`OFFSET/FETCH`, 24 a page; the page and a `COUNT(*)` read in one batch, so a page past
      the end still knows the total; `{ items, total, page, pageSize }`, 400 `invalid_filter` /
      `invalid_page`); users by kind, status and **institution** (asked 2026-11-01: a combobox that
      searches institutions, and each institution's staff count links to its people), institutions
      by status, type and district (the lookups by locale); users newest or by name, institutions by
      name or newest. Filters, order and page live in the address: a filter, the order or a page is
      a history step the back button undoes, a search replaces the address; any change but the page
      goes back to page 1. Found on the way:
      - "Something went wrong" was a closed step-up dialog (403 `stepup_required`), not the load. It
        now says "Nothing was changed: the extra check was not finished.", on every screen
      - The Owner column read `InstitutionMembers.IsOwner`, which role changes never update: now
        whoever holds an owner-level role in force, the last-owner guard's rule
      - The kind read `Users.UserType`, which only a platform role's removal ever sets to
        `InstitutionStaff`, so every owner listed as a donor: now platform staff by the stored type
        (the one the permission view trusts), then anyone holding an institution role in force
      Tests: 4 new integration tests (admin 17/17); mutations 18/18 (paging, filters, owner) + 6/6
      (kind, institution) killed; `list-query` 7 unit tests; `e2e/admin/lists.spec.ts` 5 tests, the
      step-up message's mutant killed. A commit taken while a mutation run was going held a mutant
      (`l.Rank >= 1` in c8933f5): commit only after a run ends
- [x] Translations of the new strings into `docs/translations-to-review.md`
