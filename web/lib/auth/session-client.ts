/*  The browser's half of a Sethika session (plan A2, A19, A20).

    - The access token lives in this module's memory only: never localStorage, never a cookie.
      A reload loses it, so start() refreshes once at boot from the httpOnly refresh cookie.
    - Every API call goes through fetch(). It adds the Bearer token; on 401 it refreshes once
      and retries; on 403 stepup_required it runs the registered step-up handler and retries.
    - Rotations (refresh, switch institution) are single-flight: any number of callers that
      need a new token share one request, so five failing calls cause one refresh, not five.

    Framework-free and dependency-injected (fetch, clock, cookie reader) so the state machine
    can be unit-tested without a browser. lib/auth/index.ts wires the browser's real ones.   */

export type InstitutionChoice = { slug: string; name: string };

export type SessionState =
  | { status: "loading" }
  | { status: "anonymous" }
  /** The API could not be reached at boot. Not a sign-out: the cookies may still be good. */
  | { status: "unavailable" }
  | {
      status: "authenticated";
      accessToken: string;
      /** Milliseconds on this client's clock, corrected for the server clock's offset. */
      expiresAt: number;
      institutions: InstitutionChoice[];
      activeInstitution: string | null;
    };

/** Asked to obtain a step-up (send a code, read it, verify it). Resolves true once verified. */
export type StepUpHandler = () => Promise<boolean>;

export type SwitchInstitutionResult =
  | { ok: true }
  | { ok: false; reason: "not_available" | "signed_out" | "unavailable" };

export type SessionClientOptions = {
  fetch: typeof fetch;
  /** Current time in milliseconds. */
  now: () => number;
  /** The readable `sethika-csrf` cookie's value, or null when there is none. */
  readCsrfToken: () => string | null;
  /** Waits; injectable so tests do not sleep. */
  sleep?: (ms: number) => Promise<void>;
};

export const SESSION_PATH = "/api/auth/session";
export const CSRF_HEADER = "X-CSRF-Token";

/** A20: with less than this left on the access token, refresh before asking for a step-up code. */
export const STEP_UP_MIN_REMAINING_MS = 5 * 60 * 1000;
/** Refresh ahead of expiry rather than spend a request on a guaranteed 401. */
const EXPIRY_MARGIN_MS = 10 * 1000;
/** A19: how long to wait for the winning tab's cookie before retrying a superseded refresh. */
const SUPERSEDED_WAIT_MS = 2000;
const SUPERSEDED_POLL_MS = 50;

type RotationOutcome = "rotated" | "signed_out" | "unavailable" | "not_available";

type SessionBody = {
  accessToken: string;
  accessTokenExpiresAt: string;
  institutions: InstitutionChoice[];
  activeInstitution: string | null;
};

export function createSessionClient(options: SessionClientOptions) {
  const { now, readCsrfToken } = options;
  const http = options.fetch;
  const sleep = options.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)));

  let state: SessionState = { status: "loading" };
  let rotation: Promise<RotationOutcome> | null = null;
  let stepUp: Promise<boolean> | null = null;
  let stepUpHandler: StepUpHandler | null = null;
  let started = false;
  const listeners = new Set<() => void>();

  function setState(next: SessionState) {
    state = next;
    listeners.forEach((listener) => listener());
  }

  function token(): string | null {
    return state.status === "authenticated" ? state.accessToken : null;
  }

  // ---- rotations: refresh and switch institution share one slot ------------------------------

  /** One POST to a session endpoint with the CSRF header. */
  function postSession(path: string, body?: unknown, csrf = readCsrfToken(), accessToken: string | null = null) {
    const headers: Record<string, string> = {};
    if (csrf) headers[CSRF_HEADER] = csrf;
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    return http(`${SESSION_PATH}/${path}`, {
      method: "POST",
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: "same-origin",
      cache: "no-store",
    });
  }

  async function rotate(path: string, body?: unknown): Promise<RotationOutcome> {
    for (let attempt = 0; attempt < 2; attempt++) {
      const csrf = readCsrfToken();
      // No CSRF cookie means no session cookie either (they are set and cleared together), and
      // the API would refuse the call anyway. Saves a round trip for every anonymous visitor.
      if (!csrf) return signOutLocally();

      let response: Response;
      try {
        // Plan 09, M28: the access token in hand goes with a rotation, so a step-up it holds moves to
        // its successor. The cookie alone carries none; after a reload there is no token, and none moves.
        response = await postSession(path, body, csrf, token());
      } catch {
        return "unavailable";
      }

      if (response.ok) {
        try {
          await adopt(response);
          return "rotated";
        } catch {
          return "unavailable";
        }
      }

      const code = await errorCode(response);

      // A19: another tab rotated this cookie first. Its response carries the successor cookie;
      // wait until the browser holds it (the CSRF cookie changes with every rotation), then
      // retry once with it.
      if (response.status === 409 && code === "session_superseded" && attempt === 0) {
        await waitForCookieChange(csrf);
        continue;
      }

      if (response.status === 403 && code === "institution_not_available") return "not_available";
      if (response.status === 401 || code === "csrf_failed") return signOutLocally();
      // A second superseded answer, a 429, a 5xx: nothing proves the session is dead.
      return "unavailable";
    }
    return "unavailable";
  }

  async function waitForCookieChange(previous: string) {
    for (let waited = 0; waited < SUPERSEDED_WAIT_MS; waited += SUPERSEDED_POLL_MS) {
      if (readCsrfToken() !== previous) return;
      await sleep(SUPERSEDED_POLL_MS);
    }
  }

  async function adopt(response: Response) {
    const body = (await response.json()) as SessionBody;
    // Correct for a client clock that is off: the server's Date header and its expiry share a clock.
    const serverNow = Date.parse(response.headers.get("date") ?? "");
    const offset = Number.isNaN(serverNow) ? 0 : serverNow - now();
    setState({
      status: "authenticated",
      accessToken: body.accessToken,
      expiresAt: Date.parse(body.accessTokenExpiresAt) - offset,
      institutions: body.institutions ?? [],
      activeInstitution: body.activeInstitution ?? null,
    });
  }

  function signOutLocally(): RotationOutcome {
    setState({ status: "anonymous" });
    return "signed_out";
  }

  /** Joins the rotation in flight, or starts one. Anyone who only needs a fresh token may join. */
  function sharedRotation(start: () => Promise<RotationOutcome>): Promise<RotationOutcome> {
    return rotation ?? claimRotation(start);
  }

  function claimRotation(start: () => Promise<RotationOutcome>): Promise<RotationOutcome> {
    const current = start().finally(() => {
      if (rotation === current) rotation = null;
    });
    rotation = current;
    return current;
  }

  function refresh() {
    return sharedRotation(() => rotate("refresh"));
  }

  // ---- the fetch wrapper ---------------------------------------------------------------------

  /**
   * fetch() for the Sethika API. `path` is same-origin ("/api/..."), reaching the API through
   * the Next.js rewrite (A3). The body must be re-sendable (string, FormData, Blob,
   * URLSearchParams), because a request may be sent twice.
   */
  async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
    if (state.status === "loading") await start();
    if (rotation) await rotation;

    // A token about to expire would only earn a 401; refresh first.
    if (state.status === "authenticated" && state.expiresAt - now() < EXPIRY_MARGIN_MS) {
      await refresh();
    }

    let sentWith = token();
    let response = await send(path, init, sentWith);

    if (response.status === 401) {
      // If another call already rotated while this one was in flight, its token is enough.
      if (token() === sentWith || token() === null) {
        const outcome = await refresh();
        if (outcome !== "rotated") return response;
      }
      sentWith = token();
      response = await send(path, init, sentWith);
    }

    if (response.status === 403 && (await errorCode(response)) === "stepup_required" && stepUpHandler) {
      const verified = await obtainStepUp();
      if (verified) response = await send(path, init, token());
    }

    return response;
  }

  function send(path: string, init: RequestInit, accessToken: string | null) {
    const headers = new Headers(init.headers);
    if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
    return http(path, { credentials: "same-origin", ...init, headers });
  }

  /** One step-up at a time: three refused calls open one code prompt, not three. */
  function obtainStepUp(): Promise<boolean> {
    if (!stepUp) {
      stepUp = (async () => {
        // A20: the grant is bound to this access token. With under five minutes left it would
        // die with the token, so refresh first and let the step-up buy its full window.
        if (state.status === "authenticated" && state.expiresAt - now() < STEP_UP_MIN_REMAINING_MS) {
          if ((await refresh()) !== "rotated") return false;
        }
        try {
          return (await stepUpHandler?.()) === true;
        } catch {
          return false;
        }
      })().finally(() => {
        stepUp = null;
      });
    }
    return stepUp;
  }

  // ---- lifecycle -----------------------------------------------------------------------------

  /** Silent refresh at boot. Idempotent: later calls wait for the first. */
  async function start(): Promise<void> {
    if (!started) {
      started = true;
      const outcome = await refresh();
      if (outcome === "unavailable" && state.status === "loading") setState({ status: "unavailable" });
      return;
    }
    if (rotation) await rotation;
  }

  async function logout(): Promise<void> {
    while (rotation) await rotation;
    try {
      await postSession("logout");
    } catch {
      // The server session may outlive this; the local one ends regardless.
    }
    setState({ status: "anonymous" });
  }

  /** Ends every session of this user, on every device. */
  async function logoutAll(): Promise<void> {
    while (rotation) await rotation;
    try {
      await postSession("logout-all");
    } catch {
      // As for logout.
    }
    setState({ status: "anonymous" });
  }

  /** Rotates the session onto another institution the user belongs to, by slug. */
  async function switchInstitution(slug: string): Promise<SwitchInstitutionResult> {
    // Never race a refresh with the same cookie (one of the two would be superseded), and never
    // join one either: a refresh would not switch anything. Wait for the slot, then take it.
    while (rotation) await rotation;
    const outcome = await claimRotation(() => rotate("switch-institution", { institution: slug }));
    if (outcome === "rotated") return { ok: true };
    if (outcome === "not_available" || outcome === "signed_out") return { ok: false, reason: outcome };
    return { ok: false, reason: "unavailable" };
  }

  /**
   * Leaves the institution for the user's own account (POST .../switch-personal, plan 04 P1): the
   * donor's screens hold only there (plan 02, B1). Same single slot as every rotation.
   */
  async function switchToPersonal(): Promise<SwitchInstitutionResult> {
    while (rotation) await rotation;
    const outcome = await claimRotation(() => rotate("switch-personal"));
    if (outcome === "rotated") return { ok: true };
    if (outcome === "signed_out") return { ok: false, reason: outcome };
    return { ok: false, reason: "unavailable" };
  }

  /**
   * Takes over a session that login or signup just started: their 200/201 body is the same
   * SessionResponse a refresh returns, and the refresh cookie came with it. Marks the boot as
   * done, so a later start() does not spend a refresh on a session it already holds.
   */
  async function establish(response: Response): Promise<void> {
    started = true;
    await adopt(response);
  }

  return {
    start,
    establish,
    fetch: apiFetch,
    refresh,
    logout,
    logoutAll,
    switchInstitution,
    switchToPersonal,
    getState: () => state,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    /** Registers the step-up UI. Returns a function that unregisters it. */
    setStepUpHandler(handler: StepUpHandler) {
      stepUpHandler = handler;
      return () => {
        if (stepUpHandler === handler) stepUpHandler = null;
      };
    },
  };
}

export type SessionClient = ReturnType<typeof createSessionClient>;

/** The `{ "code": ... }` of an API error (ApiErrors.cs), without consuming the caller's body. */
async function errorCode(response: Response): Promise<string | null> {
  if (!(response.headers.get("content-type") ?? "").includes("json")) return null;
  try {
    const body = (await response.clone().json()) as { code?: unknown };
    return typeof body.code === "string" ? body.code : null;
  } catch {
    return null;
  }
}
