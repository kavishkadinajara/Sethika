/*  The app's half of a Sethika session (plan F3), the mobile twin of web/lib/auth/session-client.ts.

    - The refresh token lives in the platform's secure store (Keychain / Android Keystore) behind
      TokenStore, and travels in the body to /api/auth/mobile/session/*. No cookies anywhere.
    - The access token lives in this module's memory only. A cold start loses it, so start()
      refreshes once from the stored token before anything renders that depends on identity.
    - Every API call goes through fetch(). It adds the Bearer token, and on 401 refreshes once and
      retries.
    - Rotations (refresh, switch institution) are single-flight: any number of callers that need a
      new token share one request, so five failing calls cause one refresh, not five.
    - The new refresh token is written to the store before the session is used. If the app dies
      between the server's rotation and that write, the stored token is spent: the server answers
      409 (inside its grace window) or 401 (after it, revoking the family), and either way the user
      signs in again. Nothing can recover a token the server sent and the phone never kept.

    Framework-free and dependency-injected (fetch, store, clock), so the state machine is
    unit-tested without a device. session-provider.tsx wires the real ones.                    */

export type InstitutionChoice = { slug: string; name: string };

export type SessionState =
  | { status: "loading" }
  | { status: "anonymous" }
  /** The API could not be reached at start. Not a sign-out: the stored token may still be good. */
  | { status: "unavailable" }
  | {
      status: "authenticated";
      accessToken: string;
      /** Milliseconds on this phone's clock, corrected for the server clock's offset. */
      expiresAt: number;
      institutions: InstitutionChoice[];
      activeInstitution: string | null;
    };

/** Where the refresh token is kept between launches. */
export type TokenStore = {
  read(): Promise<string | null>;
  write(token: string): Promise<void>;
  clear(): Promise<void>;
};

export type SwitchInstitutionResult =
  | { ok: true }
  | { ok: false; reason: "not_available" | "signed_out" | "unavailable" };

export type SessionClientOptions = {
  fetch: typeof fetch;
  /** The API's origin, e.g. "http://192.168.1.20:5182". Paths passed to fetch() start with "/api". */
  baseUrl: string;
  store: TokenStore;
  /** Current time in milliseconds. */
  now: () => number;
  /** Headers sent on every request (User-Agent). */
  headers?: Record<string, string>;
};

/** `response` is the API's refusal, null when no answer came. */
export type ChangePasswordResult = { ok: true } | { ok: false; response: Response | null };

export const MOBILE_SESSION_PATH = "/api/auth/mobile/session";
export const MOBILE_PASSWORD_PATH = "/api/auth/mobile/password";

/** Refresh ahead of expiry rather than spend a request on a guaranteed 401. */
const EXPIRY_MARGIN_MS = 10 * 1000;

type RotationOutcome = "rotated" | "signed_out" | "unavailable" | "not_available";

type MobileSessionBody = {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  institutions: InstitutionChoice[];
  activeInstitution: string | null;
};

export function createSessionClient(options: SessionClientOptions) {
  const { now, store, baseUrl } = options;
  const http = options.fetch;
  const commonHeaders = options.headers ?? {};

  let state: SessionState = { status: "loading" };
  let rotation: Promise<RotationOutcome> | null = null;
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

  function postSession(path: string, body: Record<string, unknown>) {
    return http(`${baseUrl}${MOBILE_SESSION_PATH}/${path}`, {
      method: "POST",
      headers: { ...commonHeaders, "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  }

  async function rotate(path: string, extra: Record<string, unknown> = {}): Promise<RotationOutcome> {
    let refreshToken: string | null;
    try {
      refreshToken = await store.read();
    } catch {
      return "unavailable";
    }
    if (!refreshToken) return signOutLocally();

    let response: Response;
    try {
      response = await postSession(path, { ...extra, refreshToken });
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
    if (response.status === 403 && code === "institution_not_available") return "not_available";

    // 401: the session is over. 409: the stored token was already spent and its successor never
    // reached the store (see the header) - rotations here are single-flight, so no other request
    // in this app can be holding it. Retrying would only turn the 409 into a family revocation.
    if (response.status === 401 || (response.status === 409 && code === "session_superseded")) {
      return signOutLocally();
    }

    // A 429, a 5xx: nothing proves the session is dead.
    return "unavailable";
  }

  async function adopt(response: Response) {
    const body = (await response.json()) as MobileSessionBody;
    await store.write(body.refreshToken);
    // Correct for a phone clock that is off: the server's Date header and its expiry share a clock.
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

  async function signOutLocally(): Promise<RotationOutcome> {
    try {
      await store.clear();
    } catch {
      // The token is dead on the server either way.
    }
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
   * fetch() for the Sethika API. `path` starts with "/api". The body must be re-sendable (a
   * string, FormData), because a request may be sent twice.
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

    return response;
  }

  function send(path: string, init: RequestInit, accessToken: string | null) {
    const headers = new Headers(init.headers);
    for (const [name, value] of Object.entries(commonHeaders)) {
      if (!headers.has(name)) headers.set(name, value);
    }
    if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
    return http(`${baseUrl}${path}`, { ...init, headers });
  }

  // ---- lifecycle -----------------------------------------------------------------------------

  /** Silent refresh at launch. Idempotent: later calls wait for the first. */
  async function start(): Promise<void> {
    if (!started) {
      started = true;
      const outcome = await refresh();
      if (outcome === "unavailable" && state.status === "loading") setState({ status: "unavailable" });
      return;
    }
    if (rotation) await rotation;
  }

  /** After "unavailable" at launch: tries the stored token again. */
  async function retry(): Promise<void> {
    if (state.status !== "unavailable") return;
    setState({ status: "loading" });
    const outcome = await refresh();
    // Read through a call: TypeScript would otherwise keep the narrowing from the guard above.
    if (outcome === "unavailable" && getState().status === "loading") setState({ status: "unavailable" });
  }

  function getState(): SessionState {
    return state;
  }

  async function endSession(path: "logout" | "logout-all"): Promise<void> {
    while (rotation) await rotation;
    try {
      const refreshToken = await store.read();
      if (refreshToken) await postSession(path, { refreshToken });
    } catch {
      // The server session may outlive this; the local one ends regardless.
    }
    await signOutLocally();
  }

  /** Ends this phone's session. */
  function logout(): Promise<void> {
    return endSession("logout");
  }

  /** Ends every session of this user, on every device and browser. */
  function logoutAll(): Promise<void> {
    return endSession("logout-all");
  }

  /** Rotates the session onto another institution the user belongs to, by slug. */
  async function switchInstitution(slug: string): Promise<SwitchInstitutionResult> {
    // Never race a refresh with the same token, and never join one either: a refresh would not
    // switch anything. Wait for the slot, then take it.
    while (rotation) await rotation;
    const outcome = await claimRotation(() => rotate("switch-institution", { institution: slug }));
    if (outcome === "rotated") return { ok: true };
    if (outcome === "not_available" || outcome === "signed_out") return { ok: false, reason: outcome };
    return { ok: false, reason: "unavailable" };
  }

  /** Rotates the session onto the person's own account (F51), the same slot as every rotation. */
  async function switchToPersonal(): Promise<SwitchInstitutionResult> {
    while (rotation) await rotation;
    const outcome = await claimRotation(() => rotate("switch-personal"));
    if (outcome === "rotated") return { ok: true };
    if (outcome === "signed_out") return { ok: false, reason: outcome };
    return { ok: false, reason: "unavailable" };
  }

  /**
   * Changes the password (F39). The change revokes every refresh token, this phone's too, and its
   * answer carries the session to go on with, so it holds the rotation slot like a rotation does:
   * a refresh sent with the old token could otherwise come back 401 after the new token was stored,
   * and sign the donor out. Calls that need a token wait for the slot and get the new one.
   */
  async function changePassword(currentPassword: string, newPassword: string): Promise<ChangePasswordResult> {
    while (rotation) await rotation;

    let result: ChangePasswordResult = { ok: false, response: null };
    const init: RequestInit = {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentPassword, newPassword }),
    };

    await claimRotation(async () => {
      let response: Response;
      try {
        response = await send(MOBILE_PASSWORD_PATH, init, token());
        if (response.status === 401) {
          // The access token ran out. A refused token never reached the password check.
          const outcome = await rotate("refresh");
          if (outcome !== "rotated") {
            result = { ok: false, response };
            return outcome;
          }
          response = await send(MOBILE_PASSWORD_PATH, init, token());
        }
      } catch {
        return "unavailable";
      }

      if (!response.ok) {
        result = { ok: false, response };
        // Nothing changed: the session is as good as it was, for whoever is waiting on this slot.
        return "rotated";
      }

      result = { ok: true };
      try {
        await adopt(response);
        return "rotated";
      } catch {
        // The password did change, so the stored token is dead and the new one is lost.
        return signOutLocally();
      }
    });

    return result;
  }

  /**
   * Takes over a session that login, code sign-in or signup just started: their 200/201 body is
   * the same MobileSessionResponse a refresh returns. Marks the launch as done, so a later
   * start() does not spend a refresh on a session it already holds.
   */
  async function establish(response: Response): Promise<void> {
    started = true;
    await adopt(response);
  }

  return {
    start,
    retry,
    establish,
    fetch: apiFetch,
    refresh,
    logout,
    logoutAll,
    switchInstitution,
    switchToPersonal,
    changePassword,
    getState,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

export type SessionClient = ReturnType<typeof createSessionClient>;

/** The `{ "code": ... }` of an API error (ApiErrors.cs), without consuming the caller's body. */
export async function errorCode(response: Response): Promise<string | null> {
  if (!(response.headers.get("content-type") ?? "").includes("json")) return null;
  try {
    const body = (await response.clone().json()) as { code?: unknown };
    return typeof body.code === "string" ? body.code : null;
  } catch {
    return null;
  }
}
