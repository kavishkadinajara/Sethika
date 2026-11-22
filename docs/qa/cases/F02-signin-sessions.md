# F02 — Sign-in, OTP sign-in, forgot / change password, sessions

Priority **P1** · Personas D1, D4, throwaways · Spec `web/e2e/qa/F02-signin-sessions.spec.ts`
Rules under test: lockout after 5 wrong passwords for 15 min (answered like a wrong password);
access token 15 min; refresh token rotates on every use, a rotated copy reused after the 10 s grace
revokes the whole family; the security stamp is checked on refresh, not on every request
(`JwtOptions`: "the longest a revoked user keeps working: fifteen minutes on their current token");
suspension bumps `PermissionsVersion`, so it bites on the next request.

| ID | QA-F02-01 |
|---|---|
| Title | A donor signs in with a password, however the number is written |
| Persona | D1 · Priority P1 · Technique: equivalence partitions |
| Steps | Sign in with `07X…`, `+947X…`, `07X XXX XXXX`; then on the login page |
| Expected | 200 each time; the page leaves `/login`; `auth.LoginHistory` has a success row; fits 390/320 px |
| Automated | "QA-F02-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-02 |
|---|---|
| Title | Five wrong passwords lock the account for 15 minutes, and the answer never says so |
| Persona | throwaway · Priority P1 · Technique: boundary (5th / 6th try), error guessing (enumeration) |
| Steps | 5 wrong passwords; then the right one (API and login page); then an unknown number |
| Expected | 401 `invalid_credentials` every time, the right password included; `LockoutEnd` 14–15 min ahead; the page says "That phone number, email or password is not right."; an unknown number answers the same |
| Automated | "QA-F02-02" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-03 |
|---|---|
| Title | A donor signs in with a texted code; an unknown number is told the same and texted nothing |
| Persona | D1, V1 · Priority P1 · Technique: happy path, error guessing (enumeration) |
| Steps | `/login/otp`: D1's number, Send code, code; then an unknown number |
| Expected | D1 signed in; both get "If 07X… is on a Sethika account, a code is on its way."; no text row for the unknown number |
| Automated | "QA-F02-03" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-04 |
|---|---|
| Title | Forgot password: a texted code sets a new one; sessions end; the old password is refused, now and as a "new" one |
| Persona | throwaway with a second device · Priority P1 · Technique: state transition |
| Steps | `/forgot-password`: number, code, mismatched confirmation, then matching; sign in old / new; refresh the other device; reset again back to the old password |
| Expected | "The two passwords don't match."; "Your password was changed"; old → 401, new → 200; other device's refresh → 401; its access token ends within its 15 min (OBS-01); reset to the old password → 400 `RecentlyUsed` |
| Automated | "QA-F02-04" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-05 |
|---|---|
| Title | Change password: the current one is checked, this browser stays signed in, other devices are signed out |
| Persona | throwaway · Priority P1 · Technique: equivalence partitions |
| Steps | `/account/password`: wrong current; new = current; then valid; refresh the other device; open My bookings |
| Expected | "That isn't your current password."; "Choose a password that is different from your current one."; "Your other devices have been signed out."; other device's refresh → 401; this browser stays signed in; the new password signs in |
| Automated | "QA-F02-05" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-06 |
|---|---|
| Title | A refresh token rotates; a copy used again after the 10 s grace kills the whole family |
| Persona | attacker with a stolen refresh token · Priority P1 · Technique: state transition, security |
| Steps | Sign in (R1); refresh (R2); refresh R1 at once; wait 11 s; refresh R1; refresh R2 |
| Expected | R2 ≠ R1; R1 within the grace → 409 `session_superseded`; after → 401 `session_expired`; R2 → 401; revoke reasons `Rotated,ReuseDetected` |
| Automated | "QA-F02-06" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-07 |
|---|---|
| Title | Two tabs refreshing the same session at once both keep the person signed in |
| Persona | throwaway · Priority P1 · Technique: concurrency |
| Steps | Two refreshes of the same token in parallel; refresh the winner's token |
| Expected | One 200, one 409 `session_superseded`; the surviving session refreshes again |
| Automated | "QA-F02-07" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-08 |
|---|---|
| Title | Sign out ends one device; "Sign out on all devices" ends every one, from the menu |
| Persona | throwaway on two phones and a browser · Priority P1 · Technique: state transition |
| Steps | Log out phone A; refresh A and B; in the browser: account menu → Sign out on all devices; refresh B; open My bookings |
| Expected | A → 401, B → 200; after "all devices" the browser is signed out, B's refresh → 401, B's access token ends within its 15 min (OBS-01); My bookings asks to sign in |
| Automated | "QA-F02-08" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F02-09 |
|---|---|
| Title | A donor suspended mid-session is refused at once; reactivated, they are back |
| Persona | D4, P1 · Priority P1 · Technique: state transition (Active → Suspended → Active) |
| Steps | D4 signed in on a phone and a browser; P1 suspends D4 (step-up); D4 uses the live token, refreshes, signs in (API and page); P1 reactivates; D4 signs in |
| Expected | Live token refused (403), refresh 401, sign-in 403 `account_not_active`, the page says "This account is not active. Please contact Sethika support."; after reactivation sign-in 200 |
| Automated | "QA-F02-09" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |
