import { CSRF_COOKIE, readCookie } from "./csrf";
import { createSessionClient } from "./session-client";

export type { InstitutionChoice, SessionState, StepUpHandler, SwitchInstitutionResult } from "./session-client";

/**
 * The browser's one session. Module state, so the access token is in JS memory only (A2) and
 * every component shares it. On the server (SSR) there is no session: it stays "loading" and
 * nothing is fetched, because the token and the cookie jar both belong to the browser.
 */
export const session = createSessionClient({
  fetch: (input, init) => globalThis.fetch(input, init),
  now: () => Date.now(),
  readCsrfToken: () => (typeof document === "undefined" ? null : readCookie(document.cookie, CSRF_COOKIE)),
});

/** fetch() for "/api/..." with the Bearer token, refresh-and-retry and step-up handled. */
export const apiFetch = session.fetch;
