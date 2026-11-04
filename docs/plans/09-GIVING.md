# Plan 09 — Phase D: giving and insight (donations, receipts, needs board, reports)

Status: **agreed 2026-11-04 (answers in §8). D.1 built 2026-11-04 (not committed; box in §7). D.2-D.5 not started.** Branch: `standing-sponsors`
(plan 08 and Phase D go to develop in one pull request, the user 2026-11-04). Decision numbers here are
**M1, M2, …**.

## 0. What the proposal promises (the report must match these)

Proposal v3, Phase D "Giving and insight": *"Donation records and receipts, online donation payments through
PayHere, the needs board with pledges, and role-based dashboards and reports."* And in its features section:

1. *"Institutions record both money and in-kind donations and issue receipts."*
2. *"They can also publish specific needs, such as fifty school books or a month of dry rations, on a public
   needs board. Donors pledge against a need, and the platform tracks progress until it is fulfilled."*
3. *"Role-based dashboards give institutions monthly booking, utilisation and donation reports, exportable for
   committee meetings."*
4. *"Donors keep a profile with their booking and donation history"* (the donation half was left to Phase D,
   `docs/report/PHASE-C.md` §1 row 4).
5. PayHere for online donation payments, sandbox in development (DECISIONS S2).

CLAUDE.md adds: money and receipts are never deleted, only voided, maker-checker (voider ≠ recorder).

**Already cut** (plan 08 Q1, the user): the gratitude wall, and part of the reports. Plan 08 §6 left "payments
or pledges for standing days" to this phase: recording a donation from a donor book row covers it (M3).

## 1. What already exists

- Tables `giving.Donations`, `giving.Needs`, `giving.Pledges` (in the schema since the start, never used).
  `Donations` already has the money/in-kind CHECKs, the void CHECK and the four-eyes CHECK
  (`VoidedBy <> RecordedBy`), tenant-consistent FKs to needs, pledges, bookings and the donor book.
- RLS: `Donations` and `Pledges` `fn_TenantOrOwner` filter + block after insert, **no block after update**;
  `Needs` public read, block after insert only, with the comment "donor pledges update pledged qty". So today
  any signed-in session could change any need's title (M9 closes it).
- Lookup types `NEED_CATEGORY`, `UNIT`, `PAYMENT_METHOD` (no values seeded).
- `core.NumberSeries` takes a per-institution scope (`ScopeKey` = institution code), gap-free inside the
  caller's transaction. `Donations.ReceiptNo` is `UNIQUE` across the platform, which a per-institution series
  would break (M2).
- The donor book with text consent (plan 08 K1, K13) and linking at sign-in (K36); the SMS outbox with
  `DedupKey`; `audit.AuditLogs`; step-up MFA for `RequiresMfa` permissions; the Day Ledger dashboards (plan 05
  X3) and `AvailabilityCalendar.DaysAsync` (plan 08 K31), which judges every slot-day of a range.

## 2. Decisions (proposed)

### Donations and receipts

| # | Decision | Why |
|---|---|---|
| M1 | **Recording a donation** (`POST /api/institution/donations`): money (amount in LKR, a payment method: cash, bank transfer, cheque, card at the counter) or in-kind (what, how many, an optional estimated value), the date it was given (Colombo, not in the future, at most 1 year back), an optional need (M11) and a note. One transaction: the receipt number (M2), the row, an audit row, the outbox message for the receipt text (M5) | Promise 1. Cash at the counter is how most giving happens today; the gateway is one more way in, never the only one (S2) |
| M2 | **Receipt numbers per institution, gap-free**: series `RECEIPT`, scope = the institution's code, `RCP-2026-000001`, drawn in the donation's transaction (a rolled-back donation gives its number back). `ReceiptNo UNIQUE` becomes `UNIQUE (InstitutionId, ReceiptNo)`; a new `PublicId` (UUID v7) addresses a receipt from the donor's side and in links | A committee audits its own book of receipts: a gap or another institution's number in it is a question. The number alone is not unique across institutions, so URLs carry `PublicId` (CLAUDE.md: never `Id`) |
| M3 | **Who gave**: a donor book row (plan 08 K1: its name, phone, and its linked account if it has one), a signed-in donor (online, M7), or a name and an optional phone typed once for a walk-in. Name and phone are snapshots on the donation, like a booking's (C6). A donation from a linked donor book row carries the account (`DonorUserId`), so it shows in that person's My giving; linking at sign-in (K36) carries their donations too, as it carries standing days | One history per person per institution; standing sponsors' payments are recorded against their donor book row (plan 08 §6) |
| M4 | **The receipt is a printable page** (`/receipts/{publicId}`, print CSS, the browser's "Save as PDF"), not a generated PDF: the institution's name and address, the receipt number, the donor's name, amount in figures or the items, method, date, who recorded it; a voided receipt shows **VOID**, the reason and when. The institution reads it through its session; the donor through their own (`DonorUserId`). It is an acknowledgement, not a tax receipt | No PDF package (QuestPDF would be a new dependency, Q5). A tax receipt needs an approved-charity status nobody has confirmed |
| M5 | **The receipt text** (`DONATION_RECEIPT`, en/si/ta drafts): the institution, the receipt number, the amount or "your gift of items". Sent to a donor with an account (as their account, in their language), or to a donor book row's phone **only with its consent ticked** (plan 08 Q2); a walk-in's typed phone is never texted. `DedupKey receipt:{InstitutionId}:{ReceiptNo}`. A void texts nothing | Same consent rule as standing sponsors; a typed number is not consent (H16) |
| M6 | **Void, never edit or delete** (`POST /api/institution/donations/{receiptNo}/void` `{reason, version}`): a different person from the one who recorded it (the CHECK already refuses it; the API answers `void_own_record` first), holding `GIVING.DONATIONS.DELETE` with **step-up MFA** (`RequiresMfa = 1`), a reason, the version read (`stale`). New columns `VoidedAt`, `RowVersion`. A voided donation stays in every list, marked, and out of every total. A mistake is corrected by voiding and recording again | CLAUDE.md's maker-checker. Step-up because voiding money is the one action that hides money from the totals |
| M7 | **Recorded money cannot be changed, by the database**: a trigger `giving.trg_Donations_Immutable` refuses any `UPDATE` that changes a column other than the void columns (and `RowVersion`), and any `DELETE`. Donations gain `AFTER UPDATE` block `fn_Tenant` (today a donor's session could update their own row) | "Never edited" then holds even against a bug in our own code. **The project's first trigger**: the alternative is the EF mapping plus a test, which a raw SQL bug would pass (Q6) |

### Online donations (PayHere sandbox)

| # | Decision | Why |
|---|---|---|
| M8 | **Give online**, from `/i/[slug]` (and from a need, M11): a signed-in donor with a confirmed phone chooses an amount (LKR 100 to 1,000,000). The API writes `giving.OnlinePayments` (new table: `OrderId` = a `PublicId`, institution, donor, amount, optional need, `Pending → Paid / Failed / Cancelled`, PayHere's payment id and status code, timestamps) and answers PayHere's checkout fields with the `hash` (MD5 of merchant id, order id, amount formatted `0.00`, currency, and the upper-case MD5 of the merchant secret); the browser posts the form to PayHere's sandbox checkout. Signing in is required | Promise 5. A payment that has not been confirmed is not a donation, so it is a separate row and gets no receipt number. Requiring an account ties the payment to a person and to the rate limit |
| M9 | **Only PayHere's notification makes a donation** (`POST /api/payments/payhere/notify`, anonymous, form-encoded): the `md5sig` is checked (merchant id + order id + amount + currency + status code + upper-case MD5 of the secret), then the amount and currency against the stored order; status `2` turns it into a donation in one transaction: a conditional `UPDATE OnlinePayments SET Status = 'Paid' WHERE OrderId = @o AND Status = 'Pending'`, then the donation (method `Online (PayHere)`, `RecordedBy` = the donor), its receipt number, the receipt text. A repeated or late notification changes nothing and answers 200. `-1`, `-2`, `-3` mark it failed or cancelled. The return page only shows what the API knows (pending until the notification arrives) | The return URL comes from the browser and can be forged; the server-to-server notification with a signature is PayHere's proof. The conditional update makes a retried notification idempotent (one receipt), as the yearly run's claim does (H3) |
| M10 | **Each institution's own merchant** (Q2, the user: option B). New table `giving.PaymentMerchants`: one row per institution (`UX_PayMerchant_Institution`), provider `PayHere`, its merchant id, its merchant secret **encrypted** (M21), enabled or not, who set it and when, `RowVersion`; RLS `fn_Tenant` (filter and block). Screen `GIVING.PAYMENTS` (`/giving/payments`): the owner enters the merchant id and secret; the secret is **write-only** (the API never returns it, only "set on 4 Nov by …"); saving needs step-up MFA. An institution without an enabled merchant shows no "Give online" and the API answers `online_giving_off`. Each order stores the merchant id it was made for; the notification (M9) finds the order, checks that `merchant_id` is that one, and checks `md5sig` with **that institution's** secret, read as the platform. Sandbox or live is the platform's setting (`Payments:PayHere:Sandbox`), the notify URL `ApiPublicUrl` from config. Locally the notification needs a tunnel (DECISIONS S2's note). Tests post signed notifications with test secrets for two institutions; no test calls PayHere. Refunds happen in the institution's own PayHere portal, which then voids the donation | Money goes straight from the donor to the institution: Sethika never holds it (P4). One institution's secret cannot sign another's notification |
| M21 | **Encrypting the merchant secret**: AES-256-GCM (`System.Security.Cryptography.AesGcm`, in .NET, no package), a random 12-byte nonce per value, the institution's id as associated data (a ciphertext copied to another institution's row does not decrypt), stored as `KeyId` + nonce + ciphertext + tag. Keys come from configuration (`Payments:Keys:{id}`, 32 bytes base64; User Secrets locally, App Service settings or Key Vault in production); `Payments:CurrentKeyId` encrypts, any listed key decrypts, so a key can be rotated. A missing key fails the start-up check, not a payment | A database backup or a SQL read never shows a merchant secret. ASP.NET Data Protection would need its key ring stored somewhere shared across instances (a new package); a few lines of AES-GCM keep that out |

### Needs board and pledges

| # | Decision | Why |
|---|---|---|
| M11 | **Needs** (`/api/institution/needs`): title, description, category, unit, quantity needed, priority (low, normal, urgent), optional deadline; `Draft → Open → Fulfilled / Closed / Cancelled`. Only an open need is public. Quantity needed can be changed while open, never below what is pledged or received (the CHECKs already say so). Reaching the quantity received turns it `Fulfilled` in the same transaction. No image (Q-cut list) | Promise 2. A need's progress is two counters the database bounds |
| M12 | **The public board**: `GET /api/needs` (anonymous, rate-limited, like search G8): open needs of active, listed institutions, filtered by district, institution type and category, urgent first, then nearest deadline; and the needs of one institution on `/i/[slug]`. The page `/needs` | "A public needs board", across institutions as the proposal's comparison table says |
| M13 | **Pledging** (`POST /api/needs/{publicId}/pledges` `{quantity, expectedDate?}`, a signed-in donor with a confirmed phone): **the counted-inventory pattern again** - one transaction: insert the pledge, then `UPDATE Needs SET QuantityPledged += @q WHERE Id = @id AND Status = 'Open' AND QuantityPledged + @q <= QuantityNeeded`; zero rows = `need_full` (or `need_closed`), and the whole pledge rolls back. `CK_Needs_Pledged` is the backstop. The donor may cancel a pledge not yet delivered; staff may cancel one (with a reason); either gives the quantity back in the same transaction. At most 5 open pledges per donor per need | The booking engine's guarantee (C2) with no Redis lock: a need is one row, so the conditional update alone serialises it; two donors pledging the last 10 books at once: one wins (forced race in the tests) |
| M14 | **Pledges and RLS**: a donor's session can read only its own pledges and may not write `Needs` (M9's block after update, `fn_Tenant`, closes today's hole). So pledging and a donor's cancel run in a scope stamped as the platform (`PlatformPledges`, as plan 08 K35's `PlatformOwnStandingDays`): the caller's user id and the need's `PublicId` are the only inputs, the pledge is written with `DonorUserId` = the caller, and nothing else is reachable. `Pledges` gains block after update `fn_TenantOrOwner` | The narrowest reach that lets a donor move a counter on another tenant's row, without leaving a public table writable by every session (unlike `SlotInstances`, C8, whose exposure is only counters) |
| M15 | **Receiving**: staff mark a pledge delivered with the quantity actually received (at most the pledge's): one transaction records an in-kind donation with the pledge and the need (a receipt, M2, and its text, M5), the pledge `Delivered`, `QuantityReceived +=` received, `QuantityPledged -=` what did not come, and `Fulfilled` when received reaches needed. Items that arrive without a pledge are recorded as an in-kind donation against the need (M1), which adds to received the same way. New column `Donations.Quantity` | "Tracks progress until it is fulfilled": progress is what arrived, and every arrival has a receipt |

### Reports and dashboards (the part kept)

| # | Decision | Why |
|---|---|---|
| M16 | **The monthly report** (`GET /api/institution/reports/monthly?month=2026-11`, screen `/reports`): bookings by status; **utilisation** per meal slot = days with at least one approved or completed booking ÷ days the slot served that month (served days from `MealSlotRules`, through `AvailabilityCalendar.DaysAsync`'s rule, not a SQL copy); money received by method, in-kind gifts by category, voided donations listed apart and out of totals; needs opened, fulfilled, still open. Dapper, SQL in embedded `.sql` files | Promise 3 word for word: "monthly booking, utilisation and donation reports" |
| M17 | **Exportable**: the same report as CSV (`?format=csv`, UTF-8 with BOM so Excel shows Sinhala and Tamil, `GIVING.REPORTS.EXPORT`) and a print layout of the page (browser "Save as PDF", `PRINT`). The donations list exports to CSV too | "Exportable for committee meetings" without a spreadsheet package |
| M18 | **Role-based dashboards**: the institution's Day Ledger gains a giving line (this month's money, open needs, pledges due this week), shown only with `GIVING.DONATIONS.VIEW`; the donor's home gains "My giving" (donations with receipts, open pledges). The platform dashboard gets nothing new | "Role-based": each role sees what its permissions allow, on the dashboards that exist (X3) |

### Permissions, texts, cut

| # | Decision | Why |
|---|---|---|
| M19 | **Catalogue**: module `GIVING` (core: on in every institution). Screens `GIVING.DONATIONS` (`/giving/donations`: VIEW, CREATE, DELETE = void with MFA, EXPORT), `GIVING.NEEDS` (`/giving/needs`: VIEW, CREATE, EDIT = open, close, cancel, change, receive and cancel pledges), `GIVING.REPORTS` (`/reports`: VIEW, EXPORT, PRINT), `GIVING.PAYMENTS` (`/giving/payments`: VIEW, EDIT with MFA; OWNER template only). Donor module `GIVE`: `MYGIVING.DONATIONS` (`/giving/mine`: VIEW, CREATE = give online and pledge, EDIT = cancel my pledge). Templates: OWNER all; MANAGER all; STAFF `DONATIONS.VIEW`, `DONATIONS.CREATE`, `NEEDS.VIEW`. DELETE on donations is "void": the catalogue's fixed actions have no VOID, and donations are never edited, so EDIT would mislead more | Same rules as every screen (anti-escalation, deny wins). Staff at the counter record gifts; voiding and reports are for those who answer to the committee |
| M20 | **Seeded lists** (not drafts; they are Sethika's own vocabulary, like reject reasons): payment methods (cash, bank transfer, cheque, card, online PayHere), need categories (dry rations, stationery, toiletries, medicine, clothing, kitchen and cleaning, other), units (pieces, packets, kg, litres, boxes, sets), each with `NameSi`/`NameTa` drafts listed in `docs/translations-to-review.md` | Lists the screens need; nothing here is a fact about a faith or an institution |

### Decided while building D.1 (2026-11-04)

| # | Decision | Why |
|---|---|---|
| M22 | **The trigger lets one column move: `DonorUserId` of a gift recorded against a donor book row**, so linking at sign-in (K36) and a phone change (K37) carry the person's receipts, voided ones too. A walk-in's or an online donor's account never moves. Who gave stays the row and the name snapshot | M3 promised linking carries donations; without this the trigger (M7) would refuse it. Only who may *read* the receipt moves |
| M23 | **A gift on a past day is stored at noon in Colombo**; a gift today keeps the moment it was recorded. The receipt year is Colombo's | A midnight timestamp falls on the previous day in UTC-based reports; noon is the same day everywhere. A receipt recorded at 01:00 on 1 January belongs to the new year's book |
| M24 | **Two receipt templates**, `DONATION_RECEIPT` (names the amount) and `DONATION_RECEIPT_ITEMS` (names no item), not one `{gift}` placeholder | No placeholder inside a sentence Sinhala or Tamil would inflect (C11), and items are typed by staff, so never sent |
| M25 | **A donor's session can never insert a donation**: the block after insert on `giving.Donations` is `fn_Tenant`, not `fn_TenantOrOwner`. Online gifts (M9) are written as the platform | The old predicate let a signed-in donor make themselves a receipt from raw SQL (now a test) |
| M26 | **Q4 is a new permission `PLATFORM.INSTITUTIONS.EDIT`** (step-up, PLATFORM_ADMIN only), `POST /api/admin/institutions/{slug}/sync-templates`, a button on the admin institutions list. It adds missing core and type-default modules (a module with a row, even off, is left), missing template roles, and each template grant a copied role does not hold **and never held** (read from `auth.RolePermissions`' temporal history), so a grant the institution took away stays away | EXECUTE is "work inside an institution"; changing its access from outside is a different act. The history is what tells "the template gained it" from "the owner removed it" |
| M27 | **The donations list's CSV moves to D.4**, with the report's CSV (one writer, one BOM rule). D.1's list filters by month on the server and by method and voided in the browser. `Kind` is `Money`/`InKind` in both directions, as stored, like booking statuses | Keeps D.1 to its promise; a month's gifts are few enough to filter in the page |
| M28 | **A step-up survives a token rotation only when the rotation also presents the access token holding it** (asked by the user 2026-11-04, after "Bring roles up to date" kept asking for a code and hit the 3-an-hour cap). `IStepUpGrants.CarryAsync` copies the grant to the new `jti` with only the time it had left (one Redis script); the session endpoints pass the bearer token the JWT handler accepted, `SessionHandler` carries it when it is the refresh token's own user; the web client sends its token with refresh and switches, never with logout. A page reload has no token in memory, so it still asks again; the dev OTP cap is 10 an hour (`appsettings.Development.json`, Console sender, no cost; production stays 3). A20 holds: the httpOnly cookie alone carries nothing | Token expiry and switching institution or account no longer throw a fresh step-up away; a thief of the cookie still inherits none (`StepUpCarryTests`, mutants C1, C2, C4 and the client's W1 killed) |

## 3. Schema changes (one publish)

- `giving.Donations`: `PublicId UNIQUEIDENTIFIER NOT NULL` (UUID v7 from code, `UNIQUE`), `Quantity DECIMAL(10,2) NULL`,
  `VoidedAt DATETIME2(3) NULL` (void CHECK extended), `RowVersion`; `UNIQUE (ReceiptNo)` →
  `UQ_Donations_Receipt (InstitutionId, ReceiptNo)`; `Note NVARCHAR(300) NULL`. The table is empty everywhere, so
  `NOT NULL` without a default is safe (the publish checks); RLS block after update; trigger (M7).
- `giving.OnlinePayments` (new, M8): RLS `fn_TenantOrOwner(InstitutionId, DonorUserId)`; the notification runs
  as the platform. Carries the `MerchantId` it was made for (M10).
- `giving.PaymentMerchants` (new, M10, M21): RLS `fn_Tenant`; the secret as `SecretKeyId`, `SecretNonce`,
  `SecretCipher` (ciphertext + tag).
- `giving.Needs`: block after update `fn_Tenant`. `giving.Pledges`: block after update; `CancelledAt`,
  `CancelledBy`, `CancelReason`, `ReceivedQuantity`, `RowVersion`; filtered index for a donor's open pledges.
- Seeds: catalogue (M19), lookup values (M20), `DONATION_RECEIPT` templates.

## 4. Tests (what must be proved)

- Receipt numbers: two institutions both start at `RCP-2026-000001`; a rolled-back donation gives its number
  back; two recorders at once get consecutive numbers.
- Void: the recorder is refused (API and, with the API bypassed, the CHECK); without step-up refused; a stale
  version refused; totals exclude voided. The trigger refuses an amount change and a delete from raw SQL.
- PayHere: a correct signature makes one donation; a wrong signature, a wrong amount, a different currency
  change nothing; the same notification twice and two at once make one donation and one receipt; a failed
  status makes none; the hash the API hands out matches PayHere's documented formula (a fixed known vector).
- Pledges: the last units pledged by two donors at once (forced race): one wins, `need_full`; cancel gives
  back; delivery with less than pledged; fulfilled on the last delivery; a donor cannot write a need's title
  (RLS block, raw SQL); a donor cannot cancel another's pledge.
- RLS from each session: the institution, another institution, the donor, another donor, anonymous.
- Report: utilisation on a slot that serves only weekdays and a blocked day; totals with a voided donation;
  CSV opens with Sinhala names intact (BOM).
- Mutations on each of the above; the browser: record cash, print, void as another person, publish a need,
  pledge from a donor, receive, see it fulfilled, give online up to PayHere's form, the report and its CSV;
  en/si/ta, desktop and 320 px.

## 5. Screens

- `/giving/donations` (institution): list (month, method, voided), record sheet (donor book picker or a
  walk-in name, money or items, need), receipt link, void sheet (reason, step-up), CSV.
- `/receipts/[publicId]`: the printable receipt (institution or the donor).
- `/giving/needs` (institution): needs with progress bars, add/edit, open/close, pledges with receive/cancel.
- `/needs` (public board) and a needs section on `/i/[slug]` with "Pledge" and "Give money".
- `/giving/mine` (donor): donations with receipts, pledges with cancel; the give sheet and PayHere's return page.
- `/reports` (institution): the month's report, print layout, CSV.

## 6. Out of scope (proposed; the report says so)

- The gratitude wall (cut, plan 08 Q1); donor rankings or any public list of donors (so `IsAnonymous` stays
  unused).
- A live sandbox payment (no PayHere account, Q1), a key vault for M21's keys, refunds from Sethika, recurring online gifts, optional platform tips
  (proposal's revenue section), card payments outside PayHere.
- Need images; pledge reminders and lapsing; donations linked to a particular booking (`BookingId` stays
  NULL); tax receipts.
- Charts beyond the report's tables, year-over-year and per-donor analytics, the demand heat map, CSR and
  platform-wide reports; Excel files (CSV only); Phase D in the mobile app.

## 7. Work breakdown (estimate ~3.25 days; each slice ends with its screens, so a late slice is cut whole)

- [x] **D.1** Schema (§3), RLS, trigger, catalogue, lookups; record a donation, receipt numbers, the receipt
  page, void with step-up, the receipt text, My giving (donations); the platform action "bring this
  institution's roles up to their templates" (Q4). Screens and e2e. ~1.0
  *Built 2026-11-04, not committed.* Schema published to Sethika_Test and the dev Sethika (user OK); every
  change an ALTER, the giving tables were empty. API: `GivingEndpoints` (`/api/institution/donations`
  list by month, methods, record, receipt, `{receiptNo}/void`; `/api/me/donations` and one receipt),
  `DonationHandler`, `DonationTexts`/`DonationSmsHandler`, `GivingQueries`, `TemplateSyncHandler`
  (+ `IRoleGrantHistory`, `IInstitutionModules.EnableMissingDefaultsAsync`). Linking (K36, K37) carries
  donations. Screens: `/giving/donations` (record sheet, void sheet, month, filters, totals),
  `/receipts/[publicId]` (print CSS hides the shell), `/giving/mine`, the admin "Bring roles up to date"
  button; 129 strings en/si/ta (drafts in `docs/translations-to-review.md`). Tests: `DonationEndpointsTests`
  24, `TemplateSyncEndpointsTests` 3, domain `DonationTests` 7, `RbacCatalogueTests` updated (+1), vitest
  `lib/giving` 6, e2e `e2e/giving/donations.spec.ts`. Mutants C# 30/32 killed (D5, D11 survive because RLS
  hides the other institution's rows: the C# filter is the second wall); SQL trigger/RLS 5/6 killed (T6,
  the trigger's `IsVoided` comparison, is equivalent: un-voiding must also clear the void columns, which
  the trigger compares too). e2e giving 4 desktop + 3 phone (incl. si/ta at 320 px), admin + shell
  neighbours green after the shell spec's donor-menu count went 6 → 7. The full `dotnet test` and full
  e2e run before the PR (D.5). Decisions M22-M27 above.
- [ ] **D.2** Needs, the public board, pledges (platform scope M14), receiving into in-kind receipts. Screens
  and e2e. ~0.7
- [ ] **D.3** PayHere sandbox: each institution's merchant settings and the encrypted secret (M10, M21), the
  order, the checkout form, the notification, the return page. ~0.8
- [ ] **D.4** The monthly report, CSV, print; dashboard lines (M18). ~0.5
- [ ] **D.5** Close-out: DECISIONS "Giving (Phase D)" + Plans row, report chapter `docs/report/PHASE-D.md`,
  full `dotnet test` + full e2e with plan 08, push and the pull request to develop (asking first). ~0.25

Cut order if late: M18's dashboard lines, then the cross-institution `/needs` page (needs stay on `/i/[slug]`),
then D.3's live sandbox walk (the signed-notification tests stay). Never cut: void maker-checker, the pledge
counter's guarantee, receipt numbering, tests and mutations.

## 8. Questions for the user

- **Q1 PayHere account.** Do you have, or will you make, a free PayHere **sandbox** merchant account
  (sandbox.payhere.lk) and install cloudflared for the notification? Without them D.3 is built and proved
  with signed test notifications, but no real sandbox payment can be shown in the viva.
- **Q2 Whose merchant?** Recommended: one Sethika sandbox merchant for the demo; the report says production
  needs each institution's own merchant so Sethika never holds donors' money (P4). Alternative: per-institution
  merchant id and secret stored encrypted now (+~0.3 day).
- **Q3 Void.** Recommended: one step, by a second person with the void permission and step-up MFA (the schema's
  CHECK). Alternative: two steps (one asks, another approves) with a request table (+~0.25 day).
- **Q4 Institutions approved before Phase D** do not hold the new permissions (cloning happens at approval, as
  K17 and SERVICES). Recommended: a platform action "bring this institution's roles up to their templates"
  (adds only what a template gained; fixes `BOOKINGS.SPONSORS` and `SERVICES` for them too; +~0.2 day; needed
  for the demo places). Alternatives: leave it as K17 did, or a one-off post-deployment script (against
  CLAUDE.md: business data in post-deployment).
- **Q5 Receipt format.** Recommended: the printable page (M4). Alternative: a real PDF with QuestPDF (a new
  package, Community licence) attached nowhere yet.
- **Q6 The trigger (M7).** Recommended: yes, the database refuses an edit to recorded money. Alternative: no
  trigger, EF mapping and tests only.

**Answers (the user, 2026-11-04):**

- **Q1:** no PayHere account. D.3 is built and proved with signed test notifications; no live sandbox
  payment is shown unless an account is made later (the report says so).
- **Q2:** option B, each institution's own merchant (M10, M21), so donors' money never passes through
  Sethika (+~0.3 day).
- **Q3:** one step: a second person with the void permission and step-up MFA (M6).
- **Q4:** yes, the platform action "bring this institution's roles up to their templates" (added to D.1).
- **Q5:** the printable page (M4), no PDF package.
- **Q6:** yes, the trigger (M7).
