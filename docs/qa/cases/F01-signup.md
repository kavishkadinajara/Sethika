# F01 — Sign-up with phone OTP

Priority **P1** · Personas D3 (new donor), V1 · Spec `web/e2e/qa/F01-signup.spec.ts`
Rules under test (code as of the run): code 6 digits, lives 5 min, 5 tries, 60 s resend cooldown,
10 sends per number per hour in Development (3 in production), 20 code requests per client per hour,
ticket 15 min, phone stored E.164; consent to every current legal document; password policy.

| ID | QA-F01-01 |
|---|---|
| Title | A first-time donor signs up on the screen and is signed in as a donor |
| Persona | D3 (desktop); fresh numbers at 390 / 320 px · Priority P1 · Technique: happy path, state transition |
| Pre-conditions | D3 has no account |
| Steps | 1. `/signup`, type the number, Send code 2. type the texted code 3. name + password, Continue 4. tick every document, Create account |
| Expected | Signed in, off `/signup`; `auth.Users` row Active, phone `+94…`, language en; only role DONOR; one consent row per current document; no sideways scroll |
| Automated | `F01-signup.spec.ts` › "QA-F01-01" (@phone: 1366, 390, 320) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-02 |
|---|---|
| Title | Every way a person writes a Sri Lankan mobile is accepted and stored alike; malformed ones are refused |
| Persona | V1 · Priority P1 · Technique: equivalence partitions |
| Steps | Request a sign-up code for `07X…`, `07X XXX XXXX`, `07X-XXX-XXXX`, `+947X…`, `947X…`, padded with spaces; a UK mobile; then `""`, 9 digits, 11 digits, letters, `079…` (unallocated), `+94001234567`; type `077010012` on the screen |
| Expected | Valid writings → 202 and the text goes to the same `+947X…`; the UK mobile → 202 (diaspora donors, by design); malformed → 400 `invalid_phone_number`; the screen shows "Enter a Sri Lankan mobile number, for example 077 123 4567." |
| Automated | "QA-F01-02" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-03 |
|---|---|
| Title | A wrong code counts down the tries; the fifth wrong one spends the code |
| Persona | D3-like new number · Priority P1 · Technique: boundary (attempts 4,3,2,1,0) |
| Steps | Request a code; verify a wrong code 5 times; then verify the right code |
| Expected | `otp_incorrect` with `attemptsRemaining` 4, 3, 2, 1, then `otp_attempts_exhausted`; the right code afterwards is refused |
| Automated | "QA-F01-03" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-04 |
|---|---|
| Title | A code is single-use, dies after 5 minutes, and belongs to its purpose |
| Persona | new numbers · Priority P1 · Technique: state transition, boundary (TTL) |
| Steps | 1. verify a right code twice 2. new code: check its TTL is 5 min, shorten the TTL in Redis to 0.3 s, verify after it 3. verify a sign-up code as a `reset` code |
| Expected | 1. second use → `otp_not_found` 2. TTL 291–300 s; expired → `otp_not_found` 3. refused (400) |
| Automated | "QA-F01-04" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-05 |
|---|---|
| Title | A new code waits out the 60-second cooldown, on the API and on the screen |
| Persona | new numbers · Priority P1 · Technique: boundary (cooldown edge ±1 s via TTL) |
| Steps | 1. request twice at once 2. move the clock to 1 s before the cooldown ends, request 3. to its end, request 4. on `/signup`, Send code |
| Expected | 1. 429 with `Retry-After` 51–60 and no second text 2. 429 3. 202 4. "New code in 0:5x" countdown instead of the resend button |
| Automated | "QA-F01-05" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-06 |
|---|---|
| Title | A number gets at most its window's sends, then a wait |
| Persona | new number · Priority P1 · Technique: boundary (9 → 10 allowed, 10 → 11 refused) |
| Steps | Set the window counter to 9 sends, request; pass the cooldown; request again |
| Expected | 10th → 202; 11th → 429 with a `Retry-After` longer than a minute |
| Automated | "QA-F01-06" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-07 |
|---|---|
| Title | A number that already has an account cannot make a second one, and the screen says so |
| Persona | D1's number in a stranger's hands · Priority P1 · Technique: error guessing |
| Steps | 1. API: verify D1's number, sign up with it 2. screen: the same, typed as `+9477…` |
| Expected | 409 `already_registered`, still one account for the number, D1 unchanged; the screen shows "An account already uses this phone number or email." |
| Automated | "QA-F01-07" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-08 |
|---|---|
| Title | A verification ticket works once, for its own number only |
| Persona | attacker replaying a ticket · Priority P1 · Technique: error guessing, security |
| Steps | Get a ticket for number A; sign up number B with it; sign up A with it; sign up A with it again |
| Expected | B → 400 `phone_not_verified` (no account); A → 201; replay → 400 `phone_not_verified` |
| Automated | "QA-F01-08" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-09 |
|---|---|
| Title | Sign-up needs every current document accepted and a password the policy takes |
| Persona | D3 making mistakes · Priority P1 · Technique: equivalence partitions |
| Steps | Sign up with: no hashes; one of two; stale hashes; password `short`; a password made of the name; a 151-character name |
| Expected | `consent_missing` ×3; `password_rejected` (with `TooShort`) ×2; `invalid_full_name`; no account made in any case |
| Automated | "QA-F01-09" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-10 |
|---|---|
| Title | One client gets 20 sign-up codes an hour; the 21st is refused and texts nobody |
| Persona | a script from one address · Priority P1 · Technique: boundary (20 / 21) |
| Steps | From one address, request codes for 21 different numbers |
| Expected | 1–20 → 202; 21st → 429 and no text row for that number |
| Automated | "QA-F01-10" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-11 |
|---|---|
| Title | A Sinhala sign-up gets its code in Sinhala |
| Persona | Sinhala-first donor · Priority P1 · Technique: equivalence (language) |
| Steps | `/si/signup`, type a number, send |
| Expected | The text holds Sinhala script and the code; the page fits 320 px |
| Automated | "QA-F01-11" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F01-12 |
|---|---|
| Title | A Sri Lankan landline is refused: it can never receive the code |
| Persona | V1 typing an office number · Priority P1 · Technique: equivalence partitions (fixed line) |
| Steps | Request sign-up codes for `0112345678`, `0812234567`, `+94 11 234 5678` |
| Expected | 400 `invalid_phone_number` (the screen asks for "a Sri Lankan mobile number"; `PhoneNumberNormalizer` exists to refuse numbers that "can never receive a text message … rather than after paying to send to it") |
| Automated | "QA-F01-12" |
| Result | **Fail — DEF-002** — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite. **Re-run after the fix: Pass** (1366 px, same day, fresh QA world; full F01-F04 49/49) |
