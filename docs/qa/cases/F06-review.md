# F06 — Registration review and approval

Priority **P1** · Personas P2 (Tharindu Bandara, PLATFORM_REVIEWER), P1 (Anjali Herath, PLATFORM_ADMIN,
decides with a step-up), A1, every institution persona for the refusals; throwaway applicants ·
Spec `web/e2e/qa/F06-review.spec.ts`. Approval is one transaction: institution, verified documents,
modules, cloned roles and grants, applicant as Owner, status history, audit row, outbox message. Each
run approves two new institutions ("QA F06 … Home"), by design of QA-F06-03 / 04.

| ID | QA-F06-01 |
|---|---|
| Title | P2's queue shows what waits for review, never drafts, and its filters hold |
| Persona | P2 · Priority P1 · Technique: equivalence partitions (status filter), boundary (page size 100 / 101, page 0) |
| Steps | A throwaway submits one and keeps a draft; API: Submitted list, all, the draft's detail, bad filters, Rejected; screen: queue, filter Approved |
| Expected | The submitted one listed, the draft neither listed nor opened (404); `Draft` / unknown status / page 0 / size 0 / 101 → 400; Rejected holds T5 only; on screen "Submitted" badge, Approved filter in the URL shows T1 and not the new one; fits 390 / 320 px |
| Automated | "QA-F06-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 |

| ID | QA-F06-02 |
|---|---|
| Title | P2 reviews on the screen — documents, checklist — and can neither approve nor reject |
| Persona | P2 · Priority P1 · Technique: role × action |
| Steps | Open, Start the review, Accept all four, answer the checklist Yes, save; then the API, after a step-up: approve, reject |
| Expected | Nothing to judge before the start; "A platform administrator makes the decision."; no Approve / Do not approve; API 403 both; status UnderReview, all documents Verified |
| Automated | "QA-F06-02" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-03 |
|---|---|
| Title | Approval waits for every check, then P1 approves on the screen and one transaction sets the institution up |
| Persona | P2, P1, throwaway applicant · Priority P1 · Technique: decision table (documents × checklist), state transition |
| Steps | Approve before the start; start twice; approve with 0, 3 of 4 verified, one not accepted (and one without remarks), unknown document; checklist empty, unknown item, one failed; all passed; approve on the screen with the step-up dialog; check the database against T3 |
| Expected | `invalid_transition`, `documents_not_verified` ×3, `remarks_required`, 404, `checklist_incomplete` ×2, `checklist_item_unknown`, status unchanged; then "Approved. The institution is on Sethika as …"; institution Active with a slug (not a number); roles, grants and enabled modules exactly as T3 (same type, no plan); 4 institution documents; the applicant a member with `IsOwner` and the OWNER role; one status-history row, one `Approve` audit row, one `InstitutionApproved` outbox row, one `ONBOARD_APPROVED` text; the applicant signs in into the institution |
| Automated | "QA-F06-03" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-04 |
|---|---|
| Title | Approving twice, one after the other or both at once, makes one institution |
| Persona | P1 · Priority P1 · Technique: concurrency |
| Steps | Reviewed application; two approves in parallel; a third; a reject after; the applicant withdraws after |
| Expected | 200 + `409 invalid_transition`; again 409 ×2; one institution, one outbox message; withdraw refused, still Approved |
| Automated | "QA-F06-04" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-05 |
|---|---|
| Title | The clarification loop from the reviewer's side: ask, answers come back, a replaced document needs checking again |
| Persona | P2, P1, throwaway applicant · Priority P1 · Technique: state transition (two rounds) |
| Steps | Ask before the start; review everything; ask `[]`, `["   "]`, `null`; ask one; P1 approves meanwhile; the applicant replaces the NIC copy and answers; P2 reads; second round; answer; P1 approves; P2's screen |
| Expected | `invalid_transition`; `400 question_required` ×3; one text per round; approve while waiting `invalid_transition`; the answer shown, the new NIC copy Pending again; approve `documents_not_verified`; both answers on the screen; two InfoRequested in the history |
| Automated | "QA-F06-05" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-06 |
|---|---|
| Title | Not approving needs a known reason, is final, and the applicant reads why |
| Persona | P1, throwaway applicant, A1 · Priority P1 · Technique: equivalence partitions (reason), state transition (final) |
| Steps | Reject with an unknown reason, `null`, nothing; with NOT_ELIGIBLE and a note; again; approve after; the applicant reads it and withdraws; A1 opens T5 |
| Expected | `reject_reason_unknown`, `invalid_details` ×2; 204; `invalid_transition` ×2; one `ONBOARD_REJECTED` text; the applicant sees reason and note, cannot withdraw; no institution; A1 sees "Your application was not approved.", "Reason: The details could not be confirmed" and the reviewer's note |
| Automated | "QA-F06-06" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-07 |
|---|---|
| Title | Nobody reviews or decides their own application |
| Persona | throwaway applicant made PLATFORM_ADMIN (SQL, as the e2e helpers), P2 · Priority P1 · Technique: error guessing (segregation of duties) |
| Steps | They start the review of their own; P2 reviews it; they step up and approve, reject |
| Expected | `403 own_application` ×3; still UnderReview |
| Automated | "QA-F06-07" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-08 |
|---|---|
| Title | Everyone but the platform is refused the review API, whatever they hold |
| Persona | D1, A1, O1, M1, S1, C1, L1, O2, O3, anonymous, a forged token · Priority P1 · Technique: role × action matrix (9 personas × 7 routes) |
| Steps | List, detail, document, start, question, approve, reject on T6; D1 opens `/si/admin/registrations` |
| Expected | 403 for each persona, 401 anonymous and forged; T6 unchanged; "මෙම ගිණුමට අයදුම්පත් සමාලෝචනය කළ නොහැක." (si) and no request numbers |
| Automated | "QA-F06-08" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F06-09 |
|---|---|
| Title | The reviewer downloads an applicant's document as an attachment, never sniffed |
| Persona | P2 · Priority P2 · Technique: error guessing (content sniffing, wrong route) |
| Steps | Download T6's NIC; an unknown type; an unknown request; the applicant's own route |
| Expected | 200 `application/pdf`, `attachment`, `nosniff`, body starts `%PDF-`; 404, 404; the applicant route not 200 |
| Automated | "QA-F06-09" |
| Result | Pass (1366 px) — run 2026-11-22 |
