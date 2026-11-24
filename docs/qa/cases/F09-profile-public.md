# F09 — Institution profile and public page `/i/[slug]`

Priority **P2** · Personas V1 (visitor), O1 (T1 Owner, Sinhala), O4 (T4 Owner, suspended tenant), D1 ·
Spec `web/e2e/qa/F09-profile-public.spec.ts`. After approval an institution changes what its public page
shows through its **meals** and its **listing**; editing the profile itself (name, address, description,
dietary notes) was deliberately not built (DECISIONS, onboarding "what was cut") — OBS-10. Every case
switches off the meals it made and puts T1's listing back as it found it.

| ID | QA-F09-01 |
|---|---|
| Title | A visitor reads T1's public page in English and Sinhala: what was published, nothing the reviewer was told |
| Persona | V1, O1 · Priority P2 · Technique: happy path, X1 (data exposure), X2 responsive |
| Pre-conditions | O1 adds a meal "QA F09 Lunch …" with Sinhala name "QA දිවා දානය" |
| Steps | `/i/<T1 slug>` and `/si/i/<T1 slug>` signed out; `GET /api/institutions/<slug>` and `/availability` |
| Expected | Name, address, the meal in the page's language (Sinhala name on `/si`); the HTML carries no institution code, office phone or registration number (E23); the API answer has no internal id, no `phone` / `email` / `officialRegistrationNo` / `institutionCode` / `status`; no sideways scroll at 1366 / 390 / 320 px |
| Automated | "QA-F09-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-24 |

| ID | QA-F09-02 |
|---|---|
| Title | What O1 changes on the meals shows on the public page at once; the profile itself has no edit |
| Persona | O1, V1 · Priority P2 · Technique: state transition (meal added → renamed → switched off) |
| Steps | O1 adds a meal (Sinhala name); the visitor opens `/si/i/…`; O1 renames it (with the version read); reload, and in English in a fresh browser; O1 switches it off; reload; `PUT /api/institution/profile` |
| Expected | Each change shows on the next load, in both languages; the switched-off meal is gone from the page and its API. The profile edit: **N/A** — no route exists (404); `INSTITUTION.PROFILE.VIEW/EDIT` and the screen `/institution/profile` are in the RBAC catalogue but nothing uses them (OBS-10) |
| Automated | "QA-F09-02" |
| Result | Pass (1366 px); profile edit N/A (OBS-10) — run 2026-11-24 |

| ID | QA-F09-03 |
|---|---|
| Title | Unlisted: reachable by its link only; listed: in the list, the search and the sitemap; a listing needs a meal |
| Persona | O1 (Sinhala screen), V1 · Priority P2 · Technique: state transition (unlisted ⇄ listed), decision table (meal × listing) |
| Steps | With no meal on: `PUT listing {listed:true}`; `{}`; O1 adds a meal; unlisted: `/api/institutions/`, `/api/search?district=KDY`, `/sitemap.xml`, `/i/<slug>`; O1 turns the switch on at `/si/bookings/slots`; then off |
| Expected | `409 no_active_slot`; `400 invalid_details ["listed"]`; unlisted: absent from all three, the page still opens (plan 03: "absent from the list but reachable by slug"); the switch shows "ඔබගේ සබැඳිය ඇති අයට පමණක්…" / "Sethika හි සොයන…"; listed: present in all three within 20 s (geo index through the outbox); unlisted again: gone from all three |
| Automated | "QA-F09-03" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F09-04 |
|---|---|
| Title | A suspended institution (T4) is not public, even when marked listed, and takes no booking |
| Persona | O4, D1, V1 · Priority P2 · Technique: state transition (institution Suspended), X1 |
| Pre-conditions | T4 let back to Active in SQL for a moment (no product path, OBS-02); O4 adds a meal and lists T4; T4 suspended again (still `IsPubliclyListed = 1`) |
| Steps | `GET /api/institutions/<T4>`, `/availability`, the list, the search (GLL); `/i/<T4>`; D1 books T4's meal by its id; O4 `PUT listing`, `GET meal-slots` |
| Expected | `404 institution_not_found` ×2 (E22, the same answer as a slug that does not exist); absent from list and search; the page 404; booking `409 institution_not_active`; O4 403 on both (a suspended tenant's permissions are gone) |
| Automated | "QA-F09-04" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F09-05 |
|---|---|
| Title | The public pages are addressed by slug, never by an internal id |
| Persona | V1 · Priority P2 · Technique: X1, error guessing |
| Steps | `/i/<T1 Id>`, `/i/INS-2026-000001`, `/i/<SLUG_WITH_UNDERSCORES>`; `GET /api/institutions/<Id>`, a 151-character slug; the list and the search JSON; every `href` on T1's page |
| Expected | 404 for each guess; no key named `id` / `…Id` (other than `publicId`) in the list or the search; no link with `/i/<number>` or `?slot=<number>` |
| Automated | "QA-F09-05" |
| Result | Pass (1366 px) — run 2026-11-24 |

Observations recorded for F09: OBS-09 (district and institution-type names are English on the Sinhala and
Tamil pages — open item, DECISIONS), OBS-10 (no profile editing after approval).
