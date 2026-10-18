# Plan 02 — Institution onboarding and approval

Branch: `3-institution-onboarding` (cut from `develop` at `481397e`, after PR #4 and #5 merged the
auth slice).

Status: **planned, nothing built.** Scope agreed on 2026-10-18: the core flow plus clarifications
(§4). Required documents, checklist items and reject reasons are seeded as clearly-marked drafts
(B9). Files go to local disk behind `IFileStorage` (B4).
Last updated 2026-10-18. Update this line and the §5 boxes whenever either changes. A fresh session
trusts them and will rebuild anything they claim is missing.

---

## 0. How to resume this from a cold start

```bash
git checkout 3-institution-onboarding
cat docs/plans/02-ONBOARDING.md    # this file
cat CLAUDE.md docs/DECISIONS.md    # the rules this plan must obey
cat docs/plans/01-AUTH.md          # §1-§2: what auth already gives you (sessions, RBAC, step-up, RLS)
dotnet build Sethika.sln && dotnet test Sethika.sln   # 367 must pass before you add anything
```

Then read §1 (what exists), §2 (decisions), §3 (open questions) and start at the first unchecked box
in §5.

The rules from plan 01 still hold: `src/Sethika.Database` is the only place schema changes are
made, every endpoint carries `[HasPermission]`, permission decisions are never re-derived in C#,
anything the database enforces is tested against real SQL Server, and every test is shown to fail
under a mutation before it counts.

---

## 1. What already exists (do not rebuild)

### Database: in the SQL project and published, mostly unused so far

| Table | What it is for | Seeded? |
|---|---|---|
| `org.RegistrationRequests` | The application. `RequestNo`, applicant snapshot (name, masked NIC, designation, phone), institution details, location (lat/long CHECKed to Sri Lanka), declaration (`DeclarationDocumentId`, `AcceptedAt`, `Ip`), `Status` with 7 values, decision fields, `CreatedInstitutionId`, `RowVersion`. CHECKs: declaration present once submitted, decided rows have a decider, approved has an institution, rejected has a reason, **reviewer and decider are never the applicant**. `UX_RegReq_ActiveRegNo`: one live application per `(type, OfficialRegistrationNo)`. | — |
| `org.RegistrationRequestDocuments` | One live file per `(request, document type)` (`UX_ReqDoc_Live`, superseded rows kept). Per-document `VerificationStatus`. | — |
| `org.RegistrationRequestOfficeBearers` | Trustees / committee members, masked NIC, `IsSignatory`. | — |
| `org.RegistrationRequestChecklist` → `org.VerificationChecklistItems` | Reviewer's pass/fail per item. | **No items** |
| `org.RegistrationRequestClarifications` | Reviewer question → applicant response (+ optional file). | — |
| `org.RegistrationRequestStatusHistory` | Every transition, who and when. | — |
| `org.DocumentTypes`, `org.InstitutionTypeRequiredDocuments` | What to upload, per type; allowed extensions, size, which fields are required. | **No rows** |
| `org.InstitutionTypes` | 6 types. | Yes (`05`) |
| `org.Institutions` | `InstitutionCode`, `Slug`, `RegistrationRequestId` (unique), status, `PermissionsVersion`, `ApprovedBy/At`. | — |
| `org.InstitutionMembers` | Membership; `UX_InstMember_Owner`: one active owner. RLS filter. | — |
| `org.InstitutionDocuments` | Verified documents copied from the request (`SourceRequestDocumentId`). RLS filter. | — |
| `org.InstitutionModules` (temporal) | Enabled modules; `vw_EffectivePermissions` reads it. | — |
| `org.InstitutionTypeDefaultModules`, `org.PlanModules` | Modules a type / plan switches on. | No rows |
| `org.InstitutionStatusHistory` | Institution lifecycle. | — |
| `core.Files` | `PublicId`, `StorageProvider` (`Local` allowed), `StorageKey`, `Sha256Hash` (`IX_Files_Hash`), `IsPrivate`, `ScanStatus`. | — |
| `core.NumberSeries` | `(SeriesCode, ScopeKey, SeriesYear)` → `LastNumber`, `Prefix`, `PadLength`. | No rows |
| `core.LegalDocuments` | TERMS and PRIVACY `0.1-draft` in en/si/ta. **No `INSTITUTION_DECLARATION`.** | Partly (`09`) |
| `core.LookupTypes` | `REJECT_REASON` type exists. **No values.** | Partly (`03`) |
| `core.OutboxMessages` | Written by signup, password change/reset, role changes. **Nothing reads it yet.** | — |
| `audit.AuditLogs` | | — |
| `auth.Roles` templates | `OWNER`, `MANAGER`, `STAFF` (`IsTemplate = 1`, `InstitutionId NULL`) with their grants; their comment already says onboarding clones them. | Yes (`10`) |

Not covered by RLS today: `org.RegistrationRequests` and its child tables (they have no
`InstitutionId`). B2 changes that for the parent.

### Backend

From plan 01: sessions, `[HasPermission]`, step-up (`RequiresMfa`), `ICurrentUserContext`
(`UserId`, `InstitutionId`, `IsPlatform`), the session-context interceptor and Dapper factory,
`ISethikaDbContext`, the rate limiter, `IPhoneNumberNormalizer`, `ILegalDocumentQueries`, consent by
`ContentSha256`, the A18 client address, the `bootstrap` platform admin, institution switching.
`Domain/Institutions/Institution.cs` is mapped (read side of the session).

**Not there:** file storage, number series, the outbox processor, Hangfire, any `Onboarding` code
(`Application/Onboarding/` is an empty folder).

---

## 2. Decisions, with the reasoning

### B1 — Who may do what: two permission sets, one per portal

The applicant is an ordinary signed-up user (`DONOR` role), as CLAUDE.md's flow says: sign up with a
phone OTP, then apply. CLAUDE.md requires a permission on every endpoint, so the applicant's
endpoints get real permissions rather than "any signed-in user":

| Portal / module / screen | Permission | Scope | MFA | Held by |
|---|---|---|---|---|
| DONOR / `APPLY` / `ONBOARDING.APPLICATION` | `ONBOARDING.APPLICATION.VIEW` | Self | 0 | `DONOR` |
| | `ONBOARDING.APPLICATION.CREATE` | Self | 0 | `DONOR` |
| | `ONBOARDING.APPLICATION.EDIT` (edit, upload, submit, withdraw, answer) | Self | 0 | `DONOR` |
| ADMIN / `ONBOARDING` / `ONBOARDING.REQUESTS` | `ONBOARDING.REQUESTS.VIEW` | Platform | 0 | `PLATFORM_ADMIN`, `PLATFORM_REVIEWER` |
| | `ONBOARDING.REQUESTS.EDIT` (start review, verify documents, checklist, ask) | Platform | 0 | `PLATFORM_ADMIN`, `PLATFORM_REVIEWER` |
| | `ONBOARDING.REQUESTS.APPROVE` | Platform | **1** | `PLATFORM_ADMIN` |
| | `ONBOARDING.REQUESTS.REJECT` | Platform | **1** | `PLATFORM_ADMIN` |

- The applicant's permissions are `Self` scope. `auth.Permissions.Scope` has no `Donor` value, and
  `CK_RoleLevelPermissions_Scope` lets any level hold a `Self` permission. That fits: applying is
  something a person does for themselves.
- The `DONOR` role is global (`InstitutionId NULL`), and `PermissionService` reads only the grants
  for the session's own institution, or the global ones when none is selected. So **the applicant's
  endpoints answer only in a personal session**. An owner who switched into an institution and then
  wants to apply for a second one switches back first. The web `/apply` page does that switch
  itself (5.5).
- The new modules `APPLY` (DONOR portal) and `ONBOARDING` (ADMIN portal) are not `IsCore`. Core
  means "on in every institution", and neither belongs to an institution.
- A new system role `PLATFORM_REVIEWER` at the existing `PLATFORM_STAFF` level: it reviews but does
  not decide. That is maker-checker at the level ceiling, and it costs one seed row. Only the
  `bootstrap` admin exists in the demo, so the admin can do every step.
- `10_RbacCatalogue.sql` gains these rows, the `DONOR` and `PLATFORM_*` ceilings (empty until now),
  and a fourth synced set: **grants to system roles** (`DONOR`, `PLATFORM_ADMIN`,
  `PLATFORM_REVIEWER`), checked against their ceilings by the same THROW pattern as template grants.
- Approve and reject are `RequiresMfa`. An approval creates a tenant with an owner, and a rejection
  can't be undone, so both deserve the step-up that `BOOKINGS.CALENDAR.APPROVE` already has.
- The DB CHECKs `CK_RegReq_ReviewerNotApplicant` and `CK_RegReq_DeciderNotApplicant` stay the last
  line. The handlers check first and answer `403 own_application`.

### B2 — The applicant's rows are protected by RLS, not only by `WHERE ApplicantUserId = @me`

Add `sec.fn_TenantOrOwner(CreatedInstitutionId, ApplicantUserId)` to `sec.TenantIsolationPolicy` on
`org.RegistrationRequests` (FILTER + BLOCK AFTER INSERT + BLOCK AFTER UPDATE). The function already
exists and needs no change:

- a platform session (`IsPlatform = 1`) sees every request (the reviewer queue);
- an applicant sees only their own;
- after approval, the new institution's sessions see the request that created it.

The child tables have no owner column. Every query and command reaches them through a join to the
parent, so the parent's filter covers them, and a test proves it: applicant B asking for A's
document by request number gets `404`, not the file. Adding a predicate recreates the policy on
publish, so D10's convergence check (`/Action:Script` shows only seed MERGEs on the second publish)
is part of the slice.

`UX_RegReq_ActiveRegNo` is enforced outside RLS, which is correct. The answer it produces
(`409 registration_no_in_use`) reveals that *some* live application uses that registration number.
That number is public record, so this is accepted.

### B3 — The request is a domain aggregate with an explicit state machine

`Domain/Onboarding/RegistrationRequest` owns the transitions. Each one returns a status-history
entry, and a transition that isn't allowed is a domain error, never a silent no-op:

```
Draft ──submit──▶ Submitted ──start review──▶ UnderReview ──approve──▶ Approved
  │                  │                          │  ▲      └──reject───▶ Rejected
  │                  │                  ask info│  │answer
  │                  │                          ▼  │
  │                  │                      InfoRequested
  └──withdraw──▶ Withdrawn ◀──withdraw──────────┘ (from Draft, Submitted or InfoRequested)
```

- The applicant may edit details, office bearers and documents only in `Draft` and
  `InfoRequested`. Replacing a document supersedes the old row (kept for history) and resets
  verification to `Pending`.
- **Submit** requires every mandatory document type for the institution type to have a live file,
  at least one office bearer, at least one signatory, and the current `INSTITUTION_DECLARATION` in
  the request's language, accepted by `ContentSha256`, the same way signup accepts its terms. It
  stamps `DeclarationIp` from the A18 client address.
- **Answer** needs every open question answered, then returns the request to `UnderReview`.
- Every write uses `RowVersion`. Two reviewers acting on one request: one wins, the other gets
  `409 stale` and reloads. This is tested by forcing the race (UPDLOCK pattern, plan 01 habits),
  not by hoping for it.
- At most 3 non-terminal applications per applicant, and creation is rate-limited per user and per
  client. One person may legitimately be a trustee of two temples, so 1 would be too strict.

Every transition is unit-tested in `Sethika.Domain.Tests`, each allowed pair and each refused one.

### B4 — Files: `IFileStorage` with a local-disk implementation

- `IFileStorage` (Application): `SaveAsync(stream) → (storageKey, sha256, size)`,
  `OpenReadAsync(key)`, `DeleteAsync(key)`. `LocalFileStorage` (Infrastructure) writes under
  `Files:LocalRoot`, outside the content root. Keys are `yyyy/MM/{guid}`, so the uploaded name never
  reaches a path. Azure Blob is a second implementation before deployment, the same arrangement as
  the SMS sender (plan 01, A14).
- Upload checks, in order: the document type is required or allowed for this institution type; the
  extension is in `DocumentTypes.AllowedExtensions`; the size is at most `MaxSizeMb` (and
  Kestrel's request limit is set per endpoint); **the first bytes match the claimed type** (PDF
  `%PDF-`, JPEG `FF D8 FF`, PNG `89 50 4E 47`); the SHA-256 is computed while the file is written.
- Order of work: write the file, then one transaction inserts `core.Files` and the request-document
  row (superseding the old one). If the transaction fails, the file is deleted in `catch`. A crash
  between the two leaves an unreferenced file on disk: harmless, and a cleanup job can sweep it
  later.
- Download goes only through a permission-checked endpoint: `Content-Disposition: attachment` with
  a sanitised name, the stored `ContentType`, `X-Content-Type-Options: nosniff`. Files are never
  served statically.
- **Virus scanning is cut.** `ScanStatus` stays `Pending` and the reviewer screen says so. In
  production this would be Defender for Storage on the Blob container. The report states it.

### B5 — Request and institution numbers come from `core.NumberSeries` inside the caller's transaction

`INumberSeries.NextAsync(series, scope, year)` runs one statement on the EF transaction's
connection: an `UPDATE ... WITH (UPDLOCK, HOLDLOCK)` that increments and returns `LastNumber`, and
inserts the row first if it is missing (MERGE with `HOLDLOCK`, so two first callers cannot both
insert). A rolled-back transaction rolls the number back too, so the series stays gap-free.

- `REQUEST` / `PLATFORM` → `REQ-2026-000123`, taken when the draft is created (`RequestNo` is NOT
  NULL). A draft that is abandoned keeps its number. Gap-free matters for receipts (Phase D), not
  for applications.
- `INSTITUTION` / `PLATFORM` → `INS-2026-000045`, taken inside the approval transaction.
- Tested with the forced-race pattern: N concurrent callers, N distinct consecutive numbers, and a
  rolled-back caller's number reused. The receipts in Phase D reuse this service unchanged.

### B6 — Approval is one transaction, and it is the slice's centrepiece

`ApproveRegistrationHandler`, one `ISethikaDbContext` transaction, RowVersion-checked:

1. Load the request: `UnderReview`, decider ≠ applicant, every mandatory document `Verified`, every
   mandatory checklist item passed. Otherwise `409` with which ones are missing.
2. `InstitutionCode` from B5. `Slug` from `ProposedName` + `City`, lower-case ASCII with `-`. A name
   with no Latin letters falls back to the code. A taken slug gets `-2`, `-3`, ... (checked in the
   transaction; `UNIQUE` on `Slug` is the final guard, and its violation answers `409` for a retry).
3. Insert `org.Institutions` (B7) with `ApprovedBy/At`.
4. Copy every `Verified` request document into `org.InstitutionDocuments`
   (`SourceRequestDocumentId`, same `FileId`: no file copy).
5. Enable modules: every `IsCore` module of the INSTITUTION portal (`Source = 'Default'`), plus `InstitutionTypeDefaultModules`
   (`Default`) and `PlanModules` of the requested plan or `FREE` (`Plan`).
6. Clone the template roles: one institution role per template (`TemplateRoleId` set, same level,
   code and name), with the template's grants copied into `auth.RolePermissions`.
7. Applicant → `org.InstitutionMembers` (`IsOwner = 1`, `Designation` from the request) and
   `auth.UserRoles` (the cloned `OWNER`). Bump the applicant's `Users.PermissionsVersion`, so their
   cached permissions die.
8. Request → `Approved`, `DecidedBy/At`, `CreatedInstitutionId`. Write a row to each status history
   (request and institution).
9. `audit.AuditLogs` row, and an outbox `InstitutionApproved` row.

Tests: the whole graph exists after approval (and the new owner's `/api/me/permissions` in that
institution equals the OWNER template); **a failure at the last step leaves nothing behind**
(forced by a unique-index collision planted before step 9, so not a mock); two concurrent approvals
create one institution; an applicant cannot approve their own request, even as a platform admin
(the DB CHECK is tested on its own too). Each step's line is mutation-tested: delete it, and a test
must go red.

### B7 — An approved institution starts `Active` and unlisted

`Status = 'Active'`, `IsPubliclyListed = 0`, `VerifiedBadgeLevel = 1`. `Onboarding` is left unused:
it would need a go-live checklist (slots set up, profile complete) that nothing defines yet.
Listing publicly is Phase B's public pages. The view already treats `Onboarding` and `Active`
alike, so this choice is cheap to revisit.

### B8 — The approved owner gets into their institution with the existing switch

No new session mechanics. The applicant's token has no institution. After approval the status page
offers "Open <institution>", which calls the existing institution switch (plan 01, A7: the list
comes from `auth.UserRoles`, which step 7 just wrote). The SMS telling them about it goes through
the outbox (5.7).

### B9 — Reference data that isn't confirmed is seeded as a marked draft *(decided 2026-10-18)*

CLAUDE.md lists required documents per institution type as an open item ("never invent data"). The
user chose the same answer as for the legal texts (plan 01, Q4): seed a small, generic set in
`12_OnboardingDrafts.sql`, where **every row's name or instructions begins with `DRAFT`** and
`docs/DECISIONS.md` records it as unconfirmed:

- `org.DocumentTypes`: registration certificate, applicant's NIC copy, proof of address, letter of
  authorisation from the governing body. All `DRAFT`.
- `org.InstitutionTypeRequiredDocuments`: the same mandatory set for all six types, for now.
- `org.VerificationChecklistItems`: registration number matches the certificate; applicant appears
  on the documents; the location pin matches the address; the office phone was answered.
- `core.LookupValues` for `REJECT_REASON`: documents incomplete, registration could not be
  verified, duplicate application, not an eligible institution, other (note required). These are
  Sethika's own review policy, not facts about institutions, so they are **not** marked DRAFT.
- `core.LegalDocuments`: `INSTITUTION_DECLARATION` `0.1-draft` in en/si/ta, si/ta added to
  `docs/translations-to-review.md`.

The flow reads all of this from the tables, so the confirmed list is a seed edit, not a code change.

### B10 — Side effects go through the outbox, and this plan builds the processor

Onboarding is the first feature that needs its outbox rows *sent* (SMS to the applicant on submit,
info requested, approved, rejected). The processor is shared with booking, so it is built once,
here:

- **Hangfire** (`Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore`: already in CLAUDE.md's
  stack, so no new architectural piece) in the API process, with SQL Server storage in the same
  database under Hangfire's own `HangFire` schema. The publish profile has
  `DropObjectsNotInSource = False`, so a dacpac publish leaves that schema alone. This is checked,
  not assumed. The dashboard sits at `/jobs`, platform-only.
- `ProcessOutboxJob`, every minute: `SELECT TOP (50) ... WITH (UPDLOCK, READPAST, ROWLOCK) WHERE
  ProcessedAt IS NULL AND Attempts < 5 ORDER BY OccurredAt`, dispatch by `EventType`, set
  `ProcessedAt`; on failure `Attempts + 1` and `LastError`. Two workers never take the same row
  (READPAST, tested by forcing it). It runs with `IsPlatform = 1`.
- An event type with no handler (`DonorSignedUp`, `PasswordReset`, ... already in the table) is
  marked processed and logged, so it never blocks the queue.
- Each SMS writes `notify.Notifications` with a `DedupKey` (`ONBOARD_APPROVED:REQ-2026-000123`).
  `UX_Notifications_Dedup` makes a re-run harmless.
- `notify.Templates` for the four messages in en/si/ta, sent in the applicant's
  `PreferredLanguage` (as the OTP SMS is).

### B11 — The location is entered as coordinates, with "use my location"

`Latitude`/`Longitude` are NOT NULL and CHECKed to Sri Lanka's bounding box. A Leaflet map picker
belongs with Phase B's map, so this slice uses two number fields, the browser's geolocation button,
and a link that opens OpenStreetMap at the pin for the reviewer to compare. No new web package.

### B12 — Masked columns and the API's own database login

`ApplicantNic` and `OfficeBearers.NicNumber` are masked. Locally the API connects with Integrated
Security as a sysadmin, so masking never applies, and a reviewer sees the NIC because of that
accident. Before deployment the API's login needs `GRANT UNMASK` (SQL Server 2022 allows it per
column) on the columns its screens show, and nothing else. Report-reading users keep the mask.
This is written in `docs/LOCAL_SETUP.md` and the deploy checklist. It is not built here, because
there is no deployment login yet.

---

## 3. Open questions

| # | Question | Blocks | Default if you say nothing |
|---|---|---|---|
| Q1 | The real required documents per institution type, and the checklist. | Nothing (B9 seeds drafts) | Drafts stay, marked, until confirmed with real institutions. |
| Q2 | Should approving also need a second admin (four eyes), not only step-up? | Nothing | No: one admin with step-up. `PLATFORM_REVIEWER` exists if the report wants maker-checker shown. |

---

## 4. Scope, and the honest warning

**In:** apply (draft → details → office bearers → documents → declaration → submit), withdraw,
the reviewer queue and detail, document verification, the checklist, clarifications (ask → answer),
approve (B6) and reject with a reason, the outbox processor and the four SMS, and the screens for
all of it in en/si/ta.

**Cut, and the report says so:** `RiskScore`/`RiskFlags` (duplicate files via `IX_Files_Hash`, a
reused NIC via `IX_OfficeBearer_Nic`, location clashes), `SlaDueAt`, `AssignedReviewerId`, evidence
files on checklist items, staff invitations (`org.StaffInvitations`), plan selection
(`RequestedPlanId` stays NULL → FREE), DS divisions (no seed; the column is nullable), virus
scanning (B4), a map picker (B11), editing the institution profile after approval.

| Slice | Effort |
|---|---|
| 5.1 schema, catalogue and draft seeds | ~0.5 day |
| 5.2 number series, file storage, the domain aggregate | ~0.5 day |
| 5.3 applicant API | ~0.75 day |
| 5.4 review and approval API | ~1 day |
| 5.5 applicant screens | ~0.75 day |
| 5.6 reviewer screens | ~0.75 day |
| 5.7 outbox processor, Hangfire, onboarding SMS | ~0.75 day |

**About 5 days.** With the submission in late October, that leaves very little for the booking
engine, which Phase A also needs, and for the report, which is the grade. Recommended order:
**5.1 → 5.2 → 5.4's approval transaction first** (the part the report's design chapter rests on),
then 5.3, 5.7, and the screens last. If time runs out, cut the screens to the minimum that
demonstrates the flow, not the tests.

---

## 5. Work breakdown

### 5.1 — Schema, catalogue and draft seeds

- [x] RLS predicate on `org.RegistrationRequests` (B2): FILTER + BLOCK AFTER INSERT/UPDATE with the
      existing `sec.fn_TenantOrOwner`. A second publish (`/Action:Script`) shows no schema statements.
- [x] `10_RbacCatalogue.sql`: the `APPLY` and `ONBOARDING` modules (not `IsCore`), both screens,
      7 permissions (applicant ones `Self` scope, see B1), 5 dependencies, the
      `DONOR`/`PLATFORM_ADMIN`/`PLATFORM_STAFF` ceilings, and a fourth synced set, the system-role
      grants, guarded by `THROW 51006`. `08_SystemRoles.sql`: `PLATFORM_REVIEWER`.
- [x] `12_OnboardingDrafts.sql` (B9) and `INSTITUTION_DECLARATION` `0.1-draft` in
      `09_LegalDocuments.sql` (each row now carries its own `EffectiveFrom`), run from
      `PostDeployment.sql`. Published to `Sethika` and `Sethika_Test`. `docs/DECISIONS.md` lists the
      drafts; the declaration's si/ta are in `docs/translations-to-review.md`.
- [x] `RbacCatalogueTests`: every template and system-role grant inside its ceiling; each system role
      holds exactly its grants, read back through the view; only approve and reject need step-up; a
      reviewer granted APPROVE in the seed stops the deploy (51006), as does an unknown role; a grant
      taken out of the seed is removed.
- [x] `RegistrationRequestSecurityTests` (7): an applicant sees only their own; the platform sees all;
      the institution sees only the request that created it, and only once linked; nobody else, and
      an unstamped connection, sees none; another applicant's UPDATE changes 0 rows; filing in
      someone else's name and handing one's application to someone else are refused (33504).
      The fixture reset now switches `FK_RegReq_Institution` off around its two deletes and back on
      `WITH CHECK`: a request and the institution it created point at each other.
      **Mutations, all killed:** each of the three predicates dropped; a donor grant deleted; REJECT
      without step-up; the reviewer granted APPROVE; the PLATFORM_STAFF ceiling narrowed; the 51006
      guard removed from the seed; the system-grant MERGE without its delete.

### 5.2 — Building blocks

- [ ] `INumberSeries` + `SqlNumberSeries` on the EF transaction's connection (B5), with forced-race
      and rollback tests.
- [ ] `IFileStorage` + `LocalFileStorage` + `UploadedFileValidator` (B4): extension, size, magic
      bytes, hash. Tests with real files, including a renamed `.exe` and an oversize PDF.
- [ ] `Domain/Onboarding/RegistrationRequest` and its children, the state machine (B3), unit-tested
      transition by transition. EF configurations for all onboarding tables (`RowVersion`, masked
      columns as plain strings).

### 5.3 — Applicant API (`/api/onboarding/applications`)

- [ ] `GET /lookups`: types, districts, required documents per type, current declaration (Dapper).
- [ ] `POST /` create draft → `{ requestNo }`; `GET /` mine; `GET /{requestNo}`.
- [ ] `PUT /{requestNo}` details; `PUT /{requestNo}/office-bearers` replaces the set (no internal ids
      exposed).
- [ ] `PUT /{requestNo}/documents/{documentTypeCode}` multipart upload / replace; `GET .../file`.
- [ ] `POST /{requestNo}/submit` (declaration hash), `POST /{requestNo}/withdraw`,
      `POST /{requestNo}/answers`.
- [ ] Tests over HTTP: the happy path, each submit refusal, edit refused outside Draft/InfoRequested,
      another applicant gets 404 everywhere, the limit of 3, rate limits, `409 registration_no_in_use`.

### 5.4 — Review and approval API (`/api/admin/registrations`)

- [ ] `GET /?status=&page=` queue (Dapper, paged, oldest submitted first); `GET /{requestNo}` detail
      with documents, office bearers, checklist, clarifications, history.
- [ ] `POST /{requestNo}/start-review`, `PUT /{requestNo}/documents/{code}/verification`,
      `PUT /{requestNo}/checklist`, `POST /{requestNo}/questions`, `GET .../documents/{code}/file`.
- [ ] `POST /{requestNo}/approve` (B6) and `POST /{requestNo}/reject` (reason code + note), both
      answering `403 stepup_required` without a step-up.
- [ ] Tests: B6's list, the stale-RowVersion race, own-application refusal, reviewer may not
      approve or reject, outbox and audit rows present.

### 5.5 — Applicant screens

- [ ] `/apply`: type → details (B11 location) → office bearers → documents → declaration → submit.
      Draft saved per step, resumable.
- [ ] `/apply/[requestNo]`: status, history, open questions with answers and document replacement,
      withdraw; after approval, "Open <institution>" (B8).
- [ ] en/si/ta strings, si/ta drafts in `docs/translations-to-review.md`; 320 px pass; Vitest for
      the step logic.

### 5.6 — Reviewer screens

- [ ] `/admin/registrations` queue with status filter.
- [ ] `/admin/registrations/[requestNo]`: details, OSM link, documents (open / verify / reject with
      remarks, `ScanStatus` shown), checklist, ask a question, approve and reject through the
      step-up dialog.

### 5.7 — Outbox processor and onboarding SMS (B10)

- [ ] Hangfire in-process with SQL storage; the second dacpac publish leaves `HangFire.*` untouched;
      `/jobs` platform-only.
- [ ] `ProcessOutboxJob`: READPAST batch, retries, unknown events marked processed; forced two-worker
      test.
- [ ] Four onboarding SMS with `DedupKey`, templates in en/si/ta; a re-run sends nothing twice.

---

## 6. Definition of done

1. `dotnet build Sethika.sln` clean (warnings are errors); `dotnet format --verify-no-changes` clean.
2. `dotnet test Sethika.sln` green; every new test was seen to fail under a mutation, and the commit
   says which.
3. `cd web && npm run lint && npm test && npm run build` clean.
4. A second `SqlPackage /Action:Script` shows seed MERGEs only.
5. A live walk-through: a new user signs up, applies, uploads, submits; the admin asks a question;
   the applicant answers; the admin verifies, steps up, approves; the applicant gets the SMS (console
   sender), opens the institution and sees the OWNER's screens.
6. `docs/DECISIONS.md` records B9's drafts and the cut list from §4.
7. PR into `develop`, Conventional Commits title, `## What` / `## Why` / `## How to verify`.

## 7. What this plan deliberately does not do

Everything in §4's cut list; re-verification (`NextReviewDue`); suspension and deactivation of an
institution; institution reports (`org.InstitutionReports`); the public institution page (Phase B).
All have tables already; none block the booking engine.
