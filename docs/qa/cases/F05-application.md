# F05 — Institution application (apply, documents, declaration, withdraw, clarifications)

Priority **P1** · Personas A1 (Imran Farook, applicant of T5 / T6), P2 (asks the questions), D1 and O1
(outsiders), throwaway applicants for the limit and registration-number cases ·
Spec `web/e2e/qa/F05-application.spec.ts`. States: Draft → Submitted → UnderReview ⇄ InfoRequested →
Approved / Rejected; Withdrawn from Draft, Submitted or InfoRequested. Every application a case opens
for A1 is closed by the case (withdrawn, or rejected by P1), so A1 stays under the limit of three.

| ID | QA-F05-01 |
|---|---|
| Title | A1 applies for a care home on the screen, from a blank form to submitted, and is texted |
| Persona | A1 · Priority P1 · Technique: state transition (Draft → Submitted), happy path at 1366 / 390 / 320 px |
| Steps | `/apply` → Start an application → details (Elders' home, Kurunegala) → office bearer → four documents → declaration → submit |
| Expected | URL becomes `/apply/REQ-…` after step 1; four required documents; "A reviewer will look at your application…"; status Submitted; one `ONBOARD_SUBMITTED` text naming the request; the current declaration recorded with the client IP; no sideways scroll |
| Automated | "QA-F05-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F05-02 |
|---|---|
| Title | Required documents and office bearers are enforced before submit, per document rule |
| Persona | A1 · Priority P1 · Technique: decision table (documents × bearers × signatory), boundary values (15 / 16 bearers, dates) |
| Steps | Draft; submit with nothing; upload an unknown type (`TRUST_DEED`, `../APPLICANT_NIC`); certificate without / non-ASCII reference, no / future / malformed issue date, expiry = issue; three of four documents; no bearers; no signatory; same NIC twice; 16 and 15 bearers; submit |
| Expected | `documents_missing`; `400 document_type_not_allowed`; `400 invalid_details` naming exactly the wrong field; `office_bearer_required`; `signatory_required`; `400 office_bearer_duplicate`; 16 → 400, 15 → 204; then 204 |
| Automated | "QA-F05-02" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-03 |
|---|---|
| Title | A document's file is checked by type, by content and by size, and the screen refuses first |
| Persona | A1 · Priority P1 · Technique: equivalence partitions (type), boundary values (5 MB, 5 MB + 1 B, 9 MB), error guessing (renamed program) |
| Steps | Upload .txt, .exe, an `MZ` program named .pdf, a PNG named .jpg, no extension, empty, 5 MB + 1 B, 9 MB; then exactly 5 MB, a PNG, a JPEG; download it; on the screen pick a .txt and a 6 MB PDF |
| Expected | `file_type_not_allowed` / `file_content_mismatch` / `file_empty` / `file_too_large`; 9 MB refused with 413 (connection closed by Kestrel), never 5xx; the three good ones 204, two superseded; download `image/jpeg`, `attachment`, `nosniff`; the screen shows "not accepted" / "larger than 5 MB" and sends no upload |
| Automated | "QA-F05-03" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-04 |
|---|---|
| Title | A draft keeps what was saved, and each detail is checked at its boundary |
| Persona | A1 · Priority P1 · Technique: boundary values (Sri Lanka's box 5.5–10.0 / 79.3–82.1, dates, year), error guessing (`javascript:` website) |
| Steps | Create with lat 5.49 / 10.01, long 79.29 / 82.11, tomorrow, 1799-12-31, next year, bad NIC, unknown type / district, bad email / website, blank name; then the edges (5.5, 82.1, this year, 1800-01-01, old NIC); rename; reopen on the screen |
| Expected | Each refusal names exactly its field and makes no draft; the edges are accepted; the rename is kept; the screen reopens at "Step 2 of 4 · Office bearers" with the town kept |
| Automated | "QA-F05-04" |
| Result | Pass (1366 px) — run 2026-11-22 (refused attempts count towards the 10 creates an hour: by design, cleared per attempt in the case) |

| ID | QA-F05-05 |
|---|---|
| Title | Submitting twice — one after the other, or two at once — submits once |
| Persona | A1 · Priority P1 · Technique: concurrency, state transition |
| Steps | Complete a draft; submit twice in a row; another draft: two submits in parallel; then try an upload and an edit |
| Expected | 204 + 409 each time; one `Submitted` in the history; one text; uploads and edits 409 once submitted |
| Automated | "QA-F05-05" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-06 |
|---|---|
| Title | The declaration must be the current one the applicant was shown |
| Persona | A1 · Priority P1 · Technique: equivalence partitions (missing, empty, wrong, one character off, upper case) |
| Steps | Submit with no hash, "", 64 zeros, the hash with its last character changed; then the right hash in capitals |
| Expected | `400 declaration_required` ×4 and still Draft; capitals accepted (204) |
| Automated | "QA-F05-06" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-07 |
|---|---|
| Title | Withdrawing: allowed from Draft, Submitted and InfoRequested, final, refused under review |
| Persona | A1, P2 · Priority P1 · Technique: state transition (every source state) |
| Steps | Withdraw a draft, then withdraw / edit / upload / submit it again; withdraw one under review; one with a question open; on the screen withdraw a submitted one (confirm) |
| Expected | 204; then `invalid_transition` / `application_not_editable`; under review `invalid_transition`; InfoRequested 204; the screen asks "Withdraw this application? It cannot be undone.", then "You withdrew this application." and no Withdraw button |
| Automated | "QA-F05-07" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-08 |
|---|---|
| Title | A1 answers the reviewer's questions — every one, after replacing a document — then it is back under review |
| Persona | A1, P2 · Priority P1 · Technique: state transition (InfoRequested → UnderReview), boundary (2000 / 2001 characters) |
| Steps | P2 asks two questions; API answers with one, with a blank, with 2001 characters; on the screen answer one only; replace the NIC copy; answer both; try again; open T6 |
| Expected | One `ONBOARD_INFO_REQUESTED` text; `questions_unanswered` ×2, 400; "Answer every question."; the new file "Waiting for review"; "A reviewer is looking at your application." with "You answered: …"; then `invalid_transition` and `application_not_editable`; T6's setup question is shown |
| Automated | "QA-F05-08" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-09 |
|---|---|
| Title | An application is only ever its applicant's: other people, other sessions and no session get nothing |
| Persona | D1, O1 (in T1), anonymous · Priority P1 · Technique: role × action, cross-user |
| Steps | D1 reads / downloads / edits / withdraws / answers / uploads on T6 and on a number that does not exist; D1's list; O1's institution session lists and creates; no token; D1 opens `/apply/<T6>` |
| Expected | Exactly the same `404 registration_not_found` for both numbers; T6 unchanged; O1 403 (B1: applications are made from the own account); anonymous 401; the screen shows "මෙම අංකය සහිත අයදුම්පතක් නැත." and no T6 name |
| Automated | "QA-F05-09" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-10 |
|---|---|
| Title | Three open applications at most, counted safely when started at once |
| Persona | throwaways · Priority P2 · Technique: boundary (3 / 4), concurrency (5 at once) |
| Steps | Three drafts; a fourth; withdraw one; a fourth again; another person sends five creates in parallel |
| Expected | `409 too_many_applications`; after withdrawing 201; in parallel exactly three 201 and two 409, three rows |
| Automated | "QA-F05-10" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F05-11 |
|---|---|
| Title | A registration number held by a live application or institution of the same type is refused, also when changed later |
| Persona | throwaway, P2 · Priority P1 · Technique: error guessing (the unique index outside the submit path) |
| Steps | Draft with T1's temple number; submit; same number as a church; a new temple application, P2 asks a question, the applicant changes its number to T1's |
| Expected | Draft allowed, submit `409 registration_no_in_use`; another type allowed; the change while InfoRequested `409 registration_no_in_use` |
| Automated | "QA-F05-11" |
| Result | Fail — DEF-004 (500 on the last step) — run 2026-11-22; **Pass after the fix** — re-run 2026-11-22 |

Notes for the report: every institution type requires the same four DRAFT document types (OBS-08); onboarding
texts go with the outbox's once-a-minute run, not at once (OBS-06).
