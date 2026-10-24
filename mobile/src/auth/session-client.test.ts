import { createSessionClient, MOBILE_SESSION_PATH, type TokenStore } from "./session-client";

/*  The mobile session client's state machine against a scripted transport and an in-memory
    secure store. The responses are the API's contracts (MobileSessionEndpoints.cs, ApiErrors.cs):
      refresh: 200 session | 401 session_expired | 409 session_superseded
      any Bearer endpoint: 401 when the token is stale                                          */

const BASE = "http://api.test";
const T0 = Date.parse("2026-10-01T00:00:00Z");
const MINUTE = 60_000;

type Call = { method: string; path: string; bearer: string | null; body: Record<string, unknown> | null };
type Handler = (call: Call) => Response | Promise<Response>;

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function sessionBody(accessToken: string, refreshToken: string, now: number, ttl = 15 * MINUTE) {
  return json(200, {
    accessToken,
    accessTokenExpiresAt: new Date(now + ttl).toISOString(),
    refreshToken,
    refreshTokenExpiresAt: new Date(now + 14 * 24 * 60 * MINUTE).toISOString(),
    institutions: [{ slug: "kandy-elders-home", name: "Elders' Home" }],
    activeInstitution: "kandy-elders-home",
  });
}

/** A client wired to a fake transport, secure store and clock that the test controls. */
function harness(stored: string | null = "refresh-1") {
  const calls: Call[] = [];
  const clock = { now: T0 };
  const vault = { token: stored, writes: [] as string[] };
  let handler: Handler = () => json(500, { code: "unscripted" });

  const store: TokenStore = {
    read: async () => vault.token,
    write: async (token) => {
      vault.token = token;
      vault.writes.push(token);
    },
    clear: async () => {
      vault.token = null;
    },
  };

  const fetchImpl = jest.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const headers = new Headers(init.headers);
    const call: Call = {
      method: init.method ?? "GET",
      path: String(input).replace(BASE, ""),
      bearer: headers.get("Authorization")?.replace(/^Bearer /, "") ?? null,
      body: typeof init.body === "string" ? (JSON.parse(init.body) as Record<string, unknown>) : null,
    };
    calls.push(call);
    return handler(call);
  });

  const client = createSessionClient({
    fetch: fetchImpl as typeof fetch,
    baseUrl: BASE,
    store,
    now: () => clock.now,
    headers: { "User-Agent": "Sethika/test" },
  });

  return {
    client,
    calls,
    clock,
    vault,
    fetchImpl,
    serve(next: Handler) {
      handler = next;
    },
    refreshes: () => calls.filter((c) => c.path === `${MOBILE_SESSION_PATH}/refresh`),
    to: (path: string) => calls.filter((c) => c.path === path),
  };
}

describe("silent refresh at launch", () => {
  it("refreshes once with the stored token in the body and keeps the successor", async () => {
    const h = harness();
    h.serve(() => sessionBody("access-1", "refresh-2", T0));

    await h.client.start();

    expect(h.refreshes()).toHaveLength(1);
    expect(h.refreshes()[0].body).toEqual({ refreshToken: "refresh-1" });
    expect(h.vault.token).toBe("refresh-2");
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "access-1" });
  });

  it("makes no request at all when nothing is stored", async () => {
    const h = harness(null);

    await h.client.start();

    expect(h.calls).toHaveLength(0);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });

  it("is unavailable, not signed out, when the API cannot be reached", async () => {
    const h = harness();
    h.serve(() => {
      throw new TypeError("Network request failed");
    });

    await h.client.start();

    expect(h.client.getState()).toEqual({ status: "unavailable" });
    expect(h.vault.token).toBe("refresh-1");
  });

  it("retry() tries the stored token again after an unreachable start", async () => {
    const h = harness();
    h.serve(() => json(503, { code: "unavailable" }));
    await h.client.start();
    expect(h.client.getState()).toEqual({ status: "unavailable" });

    h.serve(() => sessionBody("access-1", "refresh-2", T0));
    await h.client.retry();

    expect(h.client.getState()).toMatchObject({ status: "authenticated" });
  });

  it("a 401 clears the store and signs out", async () => {
    const h = harness();
    h.serve(() => json(401, { code: "session_expired" }));

    await h.client.start();

    expect(h.client.getState()).toEqual({ status: "anonymous" });
    expect(h.vault.token).toBeNull();
  });

  it("a 409 signs out instead of retrying the spent token into a family revocation", async () => {
    const h = harness();
    h.serve(() => json(409, { code: "session_superseded" }));

    await h.client.start();

    expect(h.refreshes()).toHaveLength(1);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
    expect(h.vault.token).toBeNull();
  });

  it("a 429 or 5xx keeps the stored token", async () => {
    const h = harness();
    h.serve(() => json(429, { code: "too_many_requests" }));

    await h.client.start();

    expect(h.client.getState()).toEqual({ status: "unavailable" });
    expect(h.vault.token).toBe("refresh-1");
  });
});

describe("the fetch wrapper", () => {
  it("adds the Bearer token and the app's headers", async () => {
    const h = harness();
    h.serve((call) =>
      call.path.startsWith(MOBILE_SESSION_PATH) ? sessionBody("access-1", "refresh-2", T0) : json(200, {}),
    );

    await h.client.fetch("/api/me");

    expect(h.to("/api/me")[0].bearer).toBe("access-1");
    const init = h.fetchImpl.mock.calls.at(-1)?.[1] as RequestInit;
    expect(new Headers(init.headers).get("User-Agent")).toBe("Sethika/test");
  });

  it("on 401 refreshes once and retries with the new token", async () => {
    const h = harness();
    let rotations = 0;
    h.serve((call) => {
      if (call.path.startsWith(MOBILE_SESSION_PATH)) {
        rotations++;
        return sessionBody(`access-${rotations}`, `refresh-${rotations + 1}`, T0);
      }
      return call.bearer === "access-2" ? json(200, {}) : json(401, {});
    });

    const response = await h.client.fetch("/api/me");

    expect(response.status).toBe(200);
    expect(h.refreshes().map((c) => c.body?.refreshToken)).toEqual(["refresh-1", "refresh-2"]);
    expect(h.vault.token).toBe("refresh-3");
  });

  it("shares one refresh between concurrent calls: five 401s cause one refresh", async () => {
    const h = harness();
    await (async () => {
      h.serve(() => sessionBody("access-1", "refresh-2", T0));
      await h.client.start();
    })();

    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    h.serve(async (call) => {
      if (call.path.startsWith(MOBILE_SESSION_PATH)) {
        await gate;
        return sessionBody("access-2", "refresh-3", T0);
      }
      return call.bearer === "access-2" ? json(200, {}) : json(401, {});
    });

    const pending = Array.from({ length: 5 }, () => h.client.fetch("/api/me"));
    await new Promise((resolve) => setTimeout(resolve, 0));
    release();
    const responses = await Promise.all(pending);

    expect(responses.map((r) => r.status)).toEqual([200, 200, 200, 200, 200]);
    expect(h.refreshes()).toHaveLength(2); // the launch refresh, then one shared rotation
  });

  it("refreshes before a request when the token is about to expire", async () => {
    const h = harness();
    h.serve((call) =>
      call.path.startsWith(MOBILE_SESSION_PATH)
        ? sessionBody(`access-${h.refreshes().length}`, "r", h.clock.now)
        : json(200, {}),
    );
    await h.client.start();

    h.clock.now += 15 * MINUTE - 5_000;
    await h.client.fetch("/api/me");

    expect(h.refreshes()).toHaveLength(2);
    expect(h.to("/api/me")[0].bearer).toBe("access-2");
  });

  it("a dead session returns the original 401 without retrying", async () => {
    const h = harness();
    h.serve(() => sessionBody("access-1", "refresh-2", T0));
    await h.client.start();

    h.serve((call) =>
      call.path.startsWith(MOBILE_SESSION_PATH) ? json(401, { code: "session_expired" }) : json(401, {}),
    );
    const response = await h.client.fetch("/api/me");

    expect(response.status).toBe(401);
    expect(h.to("/api/me")).toHaveLength(1);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });
});

describe("starting from login, code sign-in or signup", () => {
  it("adopts the body, stores the refresh token and skips the launch refresh", async () => {
    const h = harness(null);

    await h.client.establish(sessionBody("access-1", "refresh-1", T0));
    await h.client.start();

    expect(h.calls).toHaveLength(0);
    expect(h.vault.token).toBe("refresh-1");
    expect(h.client.getState()).toMatchObject({ status: "authenticated", activeInstitution: "kandy-elders-home" });
  });

  it("corrects the expiry for a phone clock that is off", async () => {
    const h = harness(null);
    const response = sessionBody("access-1", "refresh-1", T0);
    // The server is 10 minutes ahead of the phone.
    response.headers.set("date", new Date(T0 + 10 * MINUTE).toUTCString());
    h.clock.now = T0;

    await h.client.establish(response);

    const state = h.client.getState();
    expect(state.status === "authenticated" && state.expiresAt).toBe(T0 + 5 * MINUTE);
  });
});

describe("logout and switching institution", () => {
  async function signedIn() {
    const h = harness();
    h.serve(() => sessionBody("access-1", "refresh-2", T0));
    await h.client.start();
    return h;
  }

  it("logout posts the stored token, clears it and ends the local session", async () => {
    const h = await signedIn();
    h.serve(() => new Response(null, { status: 204 }));

    await h.client.logout();

    expect(h.to(`${MOBILE_SESSION_PATH}/logout`)[0].body).toEqual({ refreshToken: "refresh-2" });
    expect(h.vault.token).toBeNull();
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });

  it("logout ends the local session even when the API is unreachable", async () => {
    const h = await signedIn();
    h.serve(() => {
      throw new TypeError("Network request failed");
    });

    await h.client.logout();

    expect(h.vault.token).toBeNull();
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });

  it("logout-all posts the stored token to logout-all", async () => {
    const h = await signedIn();
    h.serve(() => new Response(null, { status: 204 }));

    await h.client.logoutAll();

    expect(h.to(`${MOBILE_SESSION_PATH}/logout-all`)[0].body).toEqual({ refreshToken: "refresh-2" });
    expect(h.vault.token).toBeNull();
  });

  it("switching sends the institution with the token and adopts the new session", async () => {
    const h = await signedIn();
    h.serve(() => sessionBody("access-2", "refresh-3", T0));

    const result = await h.client.switchInstitution("galle-temple");

    expect(result).toEqual({ ok: true });
    expect(h.to(`${MOBILE_SESSION_PATH}/switch-institution`)[0].body).toEqual({
      institution: "galle-temple",
      refreshToken: "refresh-2",
    });
    expect(h.vault.token).toBe("refresh-3");
  });

  it("a refused switch keeps the session and the stored token", async () => {
    const h = await signedIn();
    h.serve(() => json(403, { code: "institution_not_available" }));

    const result = await h.client.switchInstitution("galle-temple");

    expect(result).toEqual({ ok: false, reason: "not_available" });
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "access-1" });
    expect(h.vault.token).toBe("refresh-2");
  });
});
