# Plan 04 — The web app shell, dashboards and access screens

Branch: `ui-ux-overhaul`, cut on 2026-10-25 from `develop` (706bdfe, after PR #11 and #12).

Status: **U1 built, P1 fixed, U3 built (none committed). Owed: a clean full e2e run (the last
one was cut short by the API stopping), U3's backend mutation run (`mut_invites.py`, 8 mutants),
U3's e2e (`e2e/access/access.spec.ts`, written, not yet run).**
Last updated 2026-10-25. Update this line and the §4 boxes whenever
either changes; a fresh session trusts them.

Why this plan exists: on 2026-10-25 the user judged the web UI a phone site stretched onto a
desktop (one centred column, a debug panel for a home page, no navigation between screens). They
asked for a real web application: a top bar and a side menu like an ERP (two reference screenshots
of a rail + flyout menu), a dashboard for each kind of user (platform admins, institution staff
and admins, donors), every screen reachable from the menu, and roles and permissions managed from
screens. Bug fixing rides along.

The rules from plans 01-03 still hold: `src/Sethika.Database` is the only place schema changes are
made, every endpoint carries `[HasPermission]`, permission decisions are never re-derived in C#,
anything the database enforces is tested against real SQL Server and Redis, every test is shown
to fail under a mutation, and screens are tested with the `browser-test` skill (desktop + 320 px,
en/si/ta).

---

## 1. Decisions

### W1: One shell for every signed-in screen

`components/shell/AppShell.tsx` replaces `AppFrame`'s single column. Desktop (`lg` and up): a top
bar, a floating rail of section icons on the left, and a flyout panel that opens from the rail
with the section's modules as an accordion and their screens inside (the reference screenshots'
pattern). Below `lg`: the top bar with a menu button, and the same tree in a left sheet. Sign-in
screens keep their own small frame: a form needs no menu.

Signed out, the same top bar shows the public links (find a place, sign in, create an account)
and no rail, so a public page (`/institutions`, `/i/[slug]`) looks like the same product.

### W2: The menu is the RBAC catalogue's shape, filtered by permission

Rail section = portal (Dashboard, Giving = DONOR, Institution = INSTITUTION, Platform = ADMIN);
accordion group = module; link = screen, shown when the user holds the screen's `.VIEW`
permission. The tree lives in code (`components/shell/nav.ts`), not in `auth.Screens`, because
its labels need si/ta translations and its icons, and the catalogue has neither. `nav.ts` names
the same screen codes and routes as `10_RbacCatalogue.sql`; a unit test keeps the two in step.
A screen that is not built is not in the tree. Hiding a link is UX only: every endpoint keeps its
own `[HasPermission]`.

### W3: The Institution section only exists while acting for one

A user's institution permissions are only effective in an institution (plan 01). With no active
institution there is nothing in it, so the rail leaves it out instead of showing empty groups.
The top bar's context chip names the institution and switches between them when there are two
or more.

### W4: Design language: the four signatures, one step bolder

`docs/DESIGN.md` still holds. Two additions, recorded there: the rail and the dashboard's
greeting panel are brand panels (Estate Green in both themes), and a brand panel may use a tonal
gradient of its own green (never a second hue, never glass). The rail is shaped like the bowl
(gentle top corners, deeper bottom ones), so the shell carries the mark's shape.

### W5a: Full width (the user, 2026-10-25)

A centred column left both sides empty on a large screen. The page takes the whole width beside
the rail (capped at 110rem); lists add columns as the screen grows, and forms and paragraphs keep
their own readable width.

### P1: An institution's member could never reach their own account (bug, fixed)

`PermissionService` reads either the global grants or the active institution's (plan 02, B1), so
a donor's screens hold only in a personal session. But the API had no way back from an
institution to a personal session, sign-in puts anyone with exactly one institution into it, and
every refresh re-picked that institution for a session with none. So an owner could never book a
meal or see their own bookings, and the screens told them to sign out and back in, which did
not help. Fix: `POST /api/auth/session/switch-personal` (`RefreshCommand.SwitchToPersonal`), a
refresh keeps a personal session personal (it re-picks the only institution only when the one
it held was lost), "My own account" in the context switcher, and a switch button on the donor
screens' notice. Three integration tests; both mutations (switch ignored, refresh re-picking)
killed.

### W5: Dashboards read their own permissions

No new "dashboard" permission. Each card on a dashboard is backed by an endpoint that requires the
permission of the data it shows (today's sponsors: `BOOKINGS.CALENDAR.VIEW`; registrations
waiting: `ONBOARDING.REQUESTS.VIEW`; my next booking: `MYBOOKINGS.BOOKINGS.VIEW`). A user sees the
cards they could already see the screens for, and no more. Queries are Dapper (CLAUDE.md).

---

### U3 decisions: access screens and staff invitations (2026-10-25)

What exists: plan 01 5.9 built the role rules and their endpoints (create a role, set its
permissions, assign and revoke a role to someone *already* a member) with no screen and no read
endpoints. Bringing a new person in was left as "an invitation they accept"
(`org.StaffInvitations`, in the schema since plan 01, unused).

- **X1: An invitation goes to an existing Sethika account, found by phone.** As Phase E's leader
  link (V21): no account is 409 `account_not_found`, and the screen says to ask them to sign up
  first. It tells vetted staff whether a number has an account; accepted for the same reason as
  V21. No token link and no SMS in this slice; the invitee sees it in their own account.
- **X2: Schema.** `org.StaffInvitations` gains `PublicId` (never the Id in a URL),
  `InviteeUserId` (NOT NULL: X1) and a `Declined` status; one pending invitation per
  institution and person (filtered unique index). RLS changes from `fn_Tenant` to
  `fn_TenantOrOwner(InstitutionId, InviteeUserId)` with block predicates, as Bookings: the
  institution and the invitee see it, nobody else, and nobody writes one for another tenant.
- **X3: Inviting obeys the assign rules.** `ACCESS.MEMBERS.EDIT` (RequiresMfa, so step-up), the
  role strictly junior to the inviter's rank, never oneself. Someone who is already a member is
  given the role with assign, not invited (409 `already_member`).
- **X4: Accepting is one transaction,** under UPDLOCK on the invitation: pending, not expired,
  addressed to the caller; then the role (`auth.UserRoles`), the status, the caller's
  permission version and an audit row. Not `org.InstitutionMembers`: membership is read from
  `auth.UserRoles` everywhere (plan 01), and that table's RLS filter hides an earlier membership
  row from the invitee's session, so an insert would trip `UQ_InstMember`. The
  inviter's right was checked when they invited; it is not re-checked at acceptance.
- **X5: Expiry is 7 days,** read as expired from `ExpiresAt`; no job sweeps them.
- **X6: Read endpoints are Dapper:** the institution's roles (level, rank, permissions, member
  count, whether the caller may manage it), its permission catalogue (enabled modules → screens →
  permissions, with RequiresMfa and dependencies), its members with their roles, and its pending
  invitations; the invitee's own pending invitations under `/api/me`.
- **X7: The role editor is a matrix** of screens (grouped by module) × actions. A cell the caller
  may not grant (outside the level's ceiling, or not held by them) is disabled and says why;
  ticking an action ticks what it depends on, unticking VIEW unticks what needs it. The API checks
  all of it again; the matrix only avoids asking for what will be refused.

### U4 decisions: the platform administrator works inside any institution (2026-10-25)

The user, 2026-10-25: a platform super admin must be able to handle users, roles and permissions
of any organisation - create, edit - and do whatever any user can do, on every screen. The
platform keeps one set of institution screens; the administrator uses them from inside the
institution rather than through a second, platform-only copy of each.

- **Y1: Support mode.** A platform user holding `PLATFORM.INSTITUTIONS.EXECUTE` (RequiresMfa, so
  a step-up) may switch the session into any Active or Onboarding institution, from the platform's
  institutions list. The token then carries that institution with `IsPlatform`, so RLS already
  shows it (platform connections see every tenant). Each refresh re-checks the permission and the
  institution; losing either drops the session back to personal.
- **Y1a: Entering needs a step-up.** `MfaRequired` only keeps platform staff off SMS-only sign-in;
  a password sign-in is one factor. So entering is two steps: `POST
  /api/admin/institutions/{slug}/support` (`[HasPermission]` on the RequiresMfa permission, so the
  step-up middleware asks) issues a one-use ticket in Redis for two minutes and records it; then
  `switch-institution` takes a platform user into an institution they do not belong to only by
  consuming that ticket and re-reading the permission. A refresh needs no ticket, only the
  permission, read again each time.
- **Y2: Inside, they hold everything,** computed by the database, not in C#: a new branch of
  `auth.vw_EffectivePermissions` gives such a user, in every institution, every Institution-scope
  permission of its enabled modules. The permission cache, the role rules' "held by the actor"
  and the grantable-levels read all see it unchanged.
- **Y3: And they outrank everyone there** (rank 0): they may manage the Owner role, hand it out,
  and edit any role. The rank reads (the handler's check, the levels and roles queries) take 0
  for a platform user holding the support permission and no role of their own there.
- **Y4: Never silent.** A gold banner across every screen in support mode names the institution
  and says changes are recorded under the administrator's name; every write already records who
  (`AssignedBy`, `InvitedBy`, the outbox's `by`).
- **Y5: Platform screens.** `/admin/institutions` (every institution: type, district, status,
  owner, staff, "Open as platform support") on `PLATFORM.INSTITUTIONS.VIEW`; `/admin/users` (every
  account: search by name or phone, type, status, institutions and roles; create an account;
  suspend and reactivate; give and take platform roles below one's own rank) on
  `PLATFORM.USERS.VIEW / CREATE / EDIT`.
- **Y6: An account the administrator creates** has a name, a phone and a language, the DONOR role,
  and no password: the person signs in with a texted code (OTP sign-in exists), which also proves
  the phone is theirs. So it starts `PendingVerification` with `PhoneConfirmed = 0` (nobody has
  proved the number yet), OTP sign-in sends a code to such an account, and the first successful
  code confirms the phone and makes the account Active. Adding them to an institution is then an invitation (X1), from support mode
  if the institution cannot do it itself.

## 2. Scope, and the cut order

| Slice | What | Cut order if late |
|---|---|---|
| U1 | Shell: top bar, rail + flyout, mobile sheet, user menu, context chip; home page = dashboard frame (greeting, quick actions) or a public landing | never |
| U2 | Dashboard data: donor, institution and platform summaries (API + cards) | 3rd |
| U3 | Institution access screens: `/access/roles` (role list, permission matrix editor) and `/access/members` (staff, assign and revoke) on the existing role endpoints, plus the read endpoints they need | never |
| U4 | Platform administration: support mode in any institution (Y1-Y4), all institutions, all users (Y5-Y6) | never (asked for by the user, 2026-10-25) |
| U5 | Bug sweep: full e2e run, Sinhala dates on older screens, known flakes | never |
| U6 | Existing screens restyled for the wide shell (tables on desktop, page headers) | 1st |

---

## 3. Open questions (default applies if unanswered)

1. Platform roles: may a platform admin edit the platform roles' permissions from a screen, or
   only assign them to people? Default: assign only; the platform roles stay defined by the seed.
2. Known bugs the user has seen: none listed yet. Default: U5 finds them with the e2e suite.

---

## 4. Work breakdown

### U1: Shell
- [x] `lib/shell/nav.ts` tree + unit test (filtering, and codes/routes agree with the catalogue seed); 4/4 mutations killed
- [x] `AppShell` (top bar, rail, flyout, mobile sheet, user menu, context chip), messages en/si/ta
- [x] `(app)`, `(wide)` and `(home)` use it; public top bar when signed out; full width (W5a)
- [x] Home: dashboard (greeting, quick actions) when signed in, landing when signed out; `data-session` hint against a landing flash
- [x] e2e `e2e/shell/shell.spec.ts`: landing en/si/ta, menu per role, flyout, sign out (desktop + 320 px)
- [x] e2e for P1: review.spec's owner switches to their own account from `/apply` (passed); the shell spec's own P1 test still to run
- [x] Translations of the new strings into `docs/translations-to-review.md`

### U4: Platform administration
- [ ] Catalogue: modules INSTITUTIONS and USERS (ADMIN portal), their screens and permissions; ceilings and grants
- [ ] Y2 view branch; Y3 rank 0 in the three rank reads; mutation-tested against Sethika_Test
- [ ] Y1 switch into any institution + refresh re-check; tests incl. losing the permission mid-session
- [ ] `/admin/institutions` + support banner (Y4)
- [ ] `/admin/users`: list, search, create (Y6), suspend, platform roles
- [ ] e2e: an admin opens an institution, changes a role, makes someone Owner; creates a user who signs in by OTP

### U3: Access screens and invitations
- [x] X2 schema + RLS, published to Sethika and Sethika_Test (columns added at the table's end: an ALTER, no rebuild of an RLS table)
- [x] Read endpoints (X6) + 4 integration tests
- [x] Invitations (X1, X3-X5): domain (10 tests) + handler + endpoints + 9 integration tests incl. a forced accept/cancel race; 31/31 role tests green
- [ ] Backend mutations (`scratchpad/mut_invites.py`, 8 mutants) - not run yet
- [x] `/access/roles` (list, create sheet, matrix editor) and `/access/members` (staff, assign, revoke, invite, pending); matrix logic 14 unit tests, 7/7 mutations killed
- [x] Invitee: invitations card on the dashboard, accept or decline, then open the institution
- [ ] e2e `e2e/access/access.spec.ts` - written, not run yet
