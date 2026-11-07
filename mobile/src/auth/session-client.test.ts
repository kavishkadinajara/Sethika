import { createSessionClient, MOBILE_PASSWORD_PATH, MOBILE_SESSION_PATH, type TokenStore } from "./session-client";

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
  const vault = { token: stored, writes: [] as string[], refuseWrites: false };
  let handler: Handler = () => json(500, { code: "unscripted" });

  const store: TokenStore = {
    read: async () => vault.token,
    write: async (token) => {
      if (vault.refuseWrites) throw new Error("Keystore unavailable");
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

describe("changing the password", () => {
  const OLD = "Dana!2026Sethika";
  const NEW = "Pirith#Night2027";

  async function signedIn() {
    const h = harness();
    h.serve(() => sessionBody("access-1", "refresh-2", T0));
    await h.client.start();
    return h;
  }

  /** An answer the test releases by hand, so a second call can start while the first is out. */
  function held() {
    let release: (response: Response) => void = () => {};
    const answer = new Promise<Response>((resolve) => {
      release = resolve;
    });
    return { answer, release };
  }

  /** Lets everything that is not waiting on a held answer run. */
  async function flush() {
    for (let i = 0; i < 20; i++) await Promise.resolve();
  }

  it("sends both passwords with the access token, and the new refresh token is the stored one", async () => {
    const h = await signedIn();
    h.serve(() => sessionBody("access-2", "refresh-3", T0));

    const result = await h.client.changePassword(OLD, NEW);

    expect(result).toEqual({ ok: true });
    expect(h.to(MOBILE_PASSWORD_PATH)).toEqual([
      // Spelled out: the web's route would answer with a cookie the phone cannot use (F39).
      { method: "POST", path: "/api/auth/mobile/password", bearer: "access-1", body: { currentPassword: OLD, newPassword: NEW } },
    ]);
    expect(h.vault.token).toBe("refresh-3");
    expect(h.vault.writes).toEqual(["refresh-2", "refresh-3"]);
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "access-2" });
  });

  it("the next call goes out on the new session, and the next refresh spends the new token", async () => {
    const h = await signedIn();
    h.serve(() => sessionBody("access-2", "refresh-3", T0));
    await h.client.changePassword(OLD, NEW);

    h.serve((call) => (call.path === "/api/me" ? json(200, {}) : sessionBody("access-3", "refresh-4", T0)));
    await h.client.fetch("/api/me");
    await h.client.refresh();

    expect(h.to("/api/me")[0].bearer).toBe("access-2");
    expect(h.refreshes().at(-1)?.body).toEqual({ refreshToken: "refresh-3" });
  });

  it("a refusal hands the answer back and changes nothing", async () => {
    const h = await signedIn();
    h.serve(() => json(400, { code: "password_incorrect" }));

    const result = await h.client.changePassword(OLD, NEW);

    expect(result).toMatchObject({ ok: false, response: { status: 400 } });
    expect(h.vault.token).toBe("refresh-2");
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "access-1" });
    expect(h.refreshes()).toHaveLength(1);
  });

  it("an access token that ran out is refreshed, and the change sent again", async () => {
    const h = await signedIn();
    h.serve((call) => {
      if (call.path.startsWith(MOBILE_SESSION_PATH)) return sessionBody("access-2", "refresh-3", T0);
      return call.bearer === "access-1" ? json(401, {}) : sessionBody("access-3", "refresh-4", T0);
    });

    const result = await h.client.changePassword(OLD, NEW);

    expect(result).toEqual({ ok: true });
    expect(h.to(MOBILE_PASSWORD_PATH).map((call) => call.bearer)).toEqual(["access-1", "access-2"]);
    expect(h.vault.token).toBe("refresh-4");
  });

  it("a session that really ended signs out and hands back the 401", async () => {
    const h = await signedIn();
    h.serve(() => json(401, { code: "session_expired" }));

    const result = await h.client.changePassword(OLD, NEW);

    expect(result).toMatchObject({ ok: false, response: { status: 401 } });
    expect(h.to(MOBILE_PASSWORD_PATH)).toHaveLength(1);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
    expect(h.vault.token).toBeNull();
  });

  it("no answer keeps the session, and frees the slot for the next call", async () => {
    const h = await signedIn();
    h.serve(() => {
      throw new TypeError("Network request failed");
    });

    const result = await h.client.changePassword(OLD, NEW);

    expect(result).toEqual({ ok: false, response: null });
    expect(h.vault.token).toBe("refresh-2");
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "access-1" });

    h.serve(() => json(200, {}));
    expect((await h.client.fetch("/api/me")).status).toBe(200);
  });

  it("a new token the phone cannot keep ends the session, and still says the password changed", async () => {
    const h = await signedIn();
    h.serve(() => sessionBody("access-2", "refresh-3", T0));
    h.vault.refuseWrites = true;

    const result = await h.client.changePassword(OLD, NEW);

    expect(result).toEqual({ ok: true });
    expect(h.client.getState()).toEqual({ status: "anonymous" });
    expect(h.vault.token).toBeNull();
  });

  it("waits for a refresh already out, and goes with what it brought", async () => {
    const h = await signedIn();
    const refresh = held();
    h.serve((call) => (call.path === MOBILE_PASSWORD_PATH ? sessionBody("access-3", "refresh-4", T0) : refresh.answer));

    const rotating = h.client.refresh();
    const changing = h.client.changePassword(OLD, NEW);
    await flush();
    expect(h.to(MOBILE_PASSWORD_PATH)).toEqual([]);

    refresh.release(sessionBody("access-2", "refresh-3", T0));
    await Promise.all([rotating, changing]);

    expect(h.to(MOBILE_PASSWORD_PATH)[0].bearer).toBe("access-2");
    expect(h.vault.token).toBe("refresh-4");
  });

  it("a refresh asked for during the change waits for it, and never spends the old token", async () => {
    // Sent with the old token this refresh would answer 401 and sign the donor out, clearing the
    // token the change has just stored.
    const h = await signedIn();
    const change = held();
    h.serve((call) => (call.path === MOBILE_PASSWORD_PATH ? change.answer : json(401, { code: "session_expired" })));

    const changing = h.client.changePassword(OLD, NEW);
    await flush();
    const rotating = h.client.refresh();
    await flush();
    change.release(sessionBody("access-2", "refresh-3", T0));
    await Promise.all([changing, rotating]);

    expect(h.refreshes()).toHaveLength(1);
    expect(h.vault.token).toBe("refresh-3");
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "access-2" });
  });

  it("a call made during the change waits, and goes out on the new session", async () => {
    const h = await signedIn();
    const change = held();
    h.serve((call) => (call.path === MOBILE_PASSWORD_PATH ? change.answer : json(200, {})));

    const changing = h.client.changePassword(OLD, NEW);
    await flush();
    const calling = h.client.fetch("/api/me");
    await flush();
    expect(h.to("/api/me")).toEqual([]);

    change.release(sessionBody("access-2", "refresh-3", T0));
    await Promise.all([changing, calling]);

    expect(h.to("/api/me")[0].bearer).toBe("access-2");
  });
});
