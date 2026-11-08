import { describe, expect, it, vi } from "vitest";
import { createSessionClient, CSRF_HEADER } from "./session-client";

/*  The session client's state machine against a scripted transport. Each test drives the same
    responses the API's contracts define (SessionEndpoints.cs, ApiErrors.cs):
      refresh: 200 session | 401 session_expired | 409 session_superseded
      any Bearer endpoint: 401 when the token is stale, 403 stepup_required when a step-up is due */

const T0 = Date.parse("2026-10-01T00:00:00Z");
const MINUTE = 60_000;

type Call = { method: string; path: string; bearer: string | null; csrf: string | null };
type Handler = (call: Call) => Response | Promise<Response>;

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function sessionBody(accessToken: string, now: number, ttl = 15 * MINUTE) {
  return json(200, {
    accessToken,
    accessTokenExpiresAt: new Date(now + ttl).toISOString(),
    institutions: [{ slug: "kandy-elders-home", name: "Elders' Home" }],
    activeInstitution: "kandy-elders-home",
  });
}

/** A client wired to a fake transport, cookie jar and clock that the test controls. */
function harness() {
  const calls: Call[] = [];
  const inits: RequestInit[] = [];
  const clock = { now: T0 };
  const jar = { csrf: "csrf-1" as string | null };
  let handler: Handler = () => json(500, { code: "unscripted" });

  const fetchImpl = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const headers = new Headers(init.headers);
    const call: Call = {
      method: init.method ?? "GET",
      path: String(input),
      bearer: headers.get("Authorization")?.replace(/^Bearer /, "") ?? null,
      csrf: headers.get(CSRF_HEADER),
    };
    calls.push(call);
    inits.push(init);
    return handler(call);
  });

  const sleep = vi.fn(async () => {});
  const client = createSessionClient({
    fetch: fetchImpl as typeof fetch,
    now: () => clock.now,
    readCsrfToken: () => jar.csrf,
    sleep,
  });

  return {
    client,
    calls,
    inits,
    clock,
    jar,
    sleep,
    serve(next: Handler) {
      handler = next;
    },
    refreshes: () => calls.filter((c) => c.path === "/api/auth/session/refresh"),
    to: (path: string) => calls.filter((c) => c.path === path),
  };
}

/** Boots the client into a signed-in session holding `token`. */
async function signedIn(h: ReturnType<typeof harness>, token = "token-1", ttl = 15 * MINUTE) {
  h.serve((call) => (call.path.endsWith("/refresh") ? sessionBody(token, h.clock.now, ttl) : json(500, {})));
  await h.client.start();
  expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: token });
  h.calls.length = 0;
}

/** A promise the test resolves by hand, to hold a response open. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

describe("silent refresh at boot", () => {
  it("refreshes once with the CSRF cookie echoed in X-CSRF-Token", async () => {
    const h = harness();
    h.serve(() => sessionBody("token-1", h.clock.now));

    await Promise.all([h.client.start(), h.client.start(), h.client.start()]);

    expect(h.refreshes()).toEqual([
      { method: "POST", path: "/api/auth/session/refresh", bearer: null, csrf: "csrf-1" },
    ]);
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "token-1" });
  });

  it("makes no request at all for a visitor without a session cookie", async () => {
    const h = harness();
    h.jar.csrf = null;

    await h.client.start();

    expect(h.calls).toEqual([]);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });
});

describe("the fetch wrapper", () => {
  it("adds the Bearer token", async () => {
    const h = harness();
    await signedIn(h);
    h.serve(() => json(200, { ok: true }));

    await h.client.fetch("/api/me/permissions");

    expect(h.calls).toEqual([{ method: "GET", path: "/api/me/permissions", bearer: "token-1", csrf: null }]);
  });

  it("on 401 refreshes once and retries with the new token", async () => {
    const h = harness();
    await signedIn(h, "token-1");
    h.serve((call) => {
      if (call.path.endsWith("/refresh")) return sessionBody("token-2", h.clock.now);
      return call.bearer === "token-2" ? json(200, { ok: true }) : json(401, {});
    });

    const response = await h.client.fetch("/api/bookings/calendar");

    expect(response.status).toBe(200);
    expect(h.calls.map((c) => [c.path, c.bearer])).toEqual([
      ["/api/bookings/calendar", "token-1"],
      // The token in hand goes with the refresh, to carry a step-up it holds (plan 09, M28).
      ["/api/auth/session/refresh", "token-1"],
      ["/api/bookings/calendar", "token-2"],
    ]);
  });

  it("shares one refresh between concurrent calls: five 401s cause one refresh", async () => {
    const h = harness();
    await signedIn(h, "token-1");
    const refreshAnswer = deferred<Response>();
    h.serve((call) => {
      if (call.path.endsWith("/refresh")) return refreshAnswer.promise;
      return call.bearer === "token-2" ? json(200, { ok: true }) : json(401, {});
    });

    const five = Promise.all(
      ["/api/a", "/api/b", "/api/c", "/api/d", "/api/e"].map((path) => h.client.fetch(path)),
    );
    // Hold the refresh open until every call has been refused and is waiting on it.
    await vi.waitFor(() => {
      expect(h.calls.filter((c) => c.bearer === "token-1" && !c.path.endsWith("/refresh"))).toHaveLength(5);
      expect(h.refreshes().length).toBeGreaterThan(0);
    });
    refreshAnswer.resolve(sessionBody("token-2", h.clock.now));
    const responses = await five;

    expect(h.refreshes()).toHaveLength(1);
    expect(responses.map((r) => r.status)).toEqual([200, 200, 200, 200, 200]);
    expect(h.calls.filter((c) => c.bearer === "token-2")).toHaveLength(5);
  });

  it("a 401 on refresh signs out and returns the original 401 without retrying", async () => {
    const h = harness();
    await signedIn(h, "token-1");
    h.serve((call) =>
      call.path.endsWith("/refresh") ? json(401, { code: "session_expired" }) : json(401, {}),
    );

    const response = await h.client.fetch("/api/me/permissions");

    expect(response.status).toBe(401);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
    expect(h.to("/api/me/permissions")).toHaveLength(1);
    expect(h.refreshes()).toHaveLength(1);
  });
});

describe("rotations outlive the page (A19)", () => {
  /*  A page left while its refresh is in flight: the server has rotated the cookie, and a plain
      fetch is cancelled with the page, so the browser never stores the successor and the next
      page presents the spent token. keepalive lets the request finish and its Set-Cookie land. */
  it("every rotation is sent with keepalive, and nothing else is", async () => {
    const h = harness();
    await signedIn(h);
    h.inits.length = 0;
    h.serve((call) => (call.path.startsWith("/api/auth/session/") ? sessionBody("token-2", h.clock.now) : json(200, {})));

    h.clock.now += 20 * MINUTE;
    await h.client.fetch("/api/me");
    await h.client.switchInstitution("kandy-elders-home");
    await h.client.switchToPersonal();

    const rotations = h.calls
      .map((call, i) => ({ path: call.path, keepalive: h.inits[i].keepalive === true }))
      .filter((c) => c.path !== "/api/me");
    expect(rotations.map((r) => r.path)).toEqual([
      "/api/auth/session/refresh",
      "/api/auth/session/switch-institution",
      "/api/auth/session/switch-personal",
    ]);
    expect(rotations.every((r) => r.keepalive)).toBe(true);
    expect(h.inits[h.calls.findIndex((c) => c.path === "/api/me")].keepalive).not.toBe(true);
  });
});

describe("409 session_superseded (A19)", () => {
  it("waits for the winning tab's cookie and retries the refresh once with it", async () => {
    const h = harness();
    // The other tab's successful response lands while this tab waits: the CSRF cookie changes.
    h.sleep.mockImplementation(async () => {
      h.jar.csrf = "csrf-2";
    });
    h.serve((call) =>
      call.csrf === "csrf-1"
        ? json(409, { code: "session_superseded" })
        : sessionBody("token-from-new-cookie", h.clock.now),
    );

    await h.client.start();

    expect(h.refreshes().map((c) => c.csrf)).toEqual(["csrf-1", "csrf-2"]);
    expect(h.client.getState()).toMatchObject({
      status: "authenticated",
      accessToken: "token-from-new-cookie",
    });
  });

  it("does not sign out when the second attempt is superseded too", async () => {
    const h = harness();
    h.serve(() => json(409, { code: "session_superseded" }));

    await h.client.start();

    expect(h.refreshes()).toHaveLength(2);
    expect(h.client.getState()).toEqual({ status: "unavailable" });
  });
});

describe("403 stepup_required (A20)", () => {
  function stepUpServer(h: ReturnType<typeof harness>, granted: { token: string | null }) {
    h.serve((call) => {
      if (call.path.endsWith("/refresh")) return sessionBody("token-2", h.clock.now);
      if (call.path === "/api/bookings/BK-1/approve") {
        return call.bearer === granted.token ? json(200, { ok: true }) : json(403, { code: "stepup_required" });
      }
      return json(500, {});
    });
  }

  it("runs the step-up handler, then retries the request", async () => {
    const h = harness();
    await signedIn(h, "token-1", 15 * MINUTE);
    const granted = { token: null as string | null };
    stepUpServer(h, granted);
    const handler = vi.fn(async () => {
      granted.token = "token-1"; // the grant is bound to the token that stepped up
      return true;
    });
    h.client.setStepUpHandler(handler);

    const response = await h.client.fetch("/api/bookings/BK-1/approve", { method: "POST" });

    expect(handler).toHaveBeenCalledTimes(1);
    expect(response.status).toBe(200);
    expect(h.to("/api/bookings/BK-1/approve").map((c) => c.bearer)).toEqual(["token-1", "token-1"]);
    expect(h.refreshes()).toHaveLength(0);
  });

  it("with under five minutes left, refreshes before asking for the code", async () => {
    const h = harness();
    await signedIn(h, "token-1", 4 * MINUTE);
    const granted = { token: null as string | null };
    stepUpServer(h, granted);
    const order: string[] = [];
    const handler = vi.fn(async () => {
      order.push(`handler while holding ${(h.client.getState() as { accessToken: string }).accessToken}`);
      granted.token = "token-2";
      return true;
    });
    h.client.setStepUpHandler(handler);

    const response = await h.client.fetch("/api/bookings/BK-1/approve", { method: "POST" });

    expect(response.status).toBe(200);
    expect(h.refreshes()).toHaveLength(1);
    expect(order).toEqual(["handler while holding token-2"]);
    expect(h.to("/api/bookings/BK-1/approve").map((c) => c.bearer)).toEqual(["token-1", "token-2"]);
  });

  it("asks once when several calls need a step-up at the same time", async () => {
    const h = harness();
    await signedIn(h, "token-1", 15 * MINUTE);
    const granted = { token: null as string | null };
    stepUpServer(h, granted);
    const handler = vi.fn(async () => {
      granted.token = "token-1";
      return true;
    });
    h.client.setStepUpHandler(handler);

    const responses = await Promise.all(
      [1, 2, 3].map(() => h.client.fetch("/api/bookings/BK-1/approve", { method: "POST" })),
    );

    expect(handler).toHaveBeenCalledTimes(1);
    expect(responses.map((r) => r.status)).toEqual([200, 200, 200]);
  });

  it("returns the 403 when the step-up is cancelled", async () => {
    const h = harness();
    await signedIn(h, "token-1", 15 * MINUTE);
    stepUpServer(h, { token: null });
    h.client.setStepUpHandler(async () => false);

    const response = await h.client.fetch("/api/bookings/BK-1/approve", { method: "POST" });

    expect(response.status).toBe(403);
    expect(await response.json()).toEqual({ code: "stepup_required" });
    expect(h.to("/api/bookings/BK-1/approve")).toHaveLength(1);
  });
});

describe("starting from login or signup", () => {
  it("adopts the session body and skips the boot refresh", async () => {
    const h = harness();
    h.serve(() => json(500, {}));

    await h.client.establish(sessionBody("token-from-login", h.clock.now));
    await h.client.start();

    expect(h.client.getState()).toMatchObject({
      status: "authenticated",
      accessToken: "token-from-login",
      activeInstitution: "kandy-elders-home",
    });
    expect(h.calls).toEqual([]);
  });
});

describe("logout and switching institution", () => {
  it("logout posts with the CSRF header and ends the local session", async () => {
    const h = harness();
    await signedIn(h);
    h.serve(() => new Response(null, { status: 204 }));

    await h.client.logout();

    expect(h.calls).toEqual([
      { method: "POST", path: "/api/auth/session/logout", bearer: null, csrf: "csrf-1" },
    ]);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });

  it("logout-all posts with the CSRF header and ends the local session", async () => {
    const h = harness();
    await signedIn(h);
    h.serve(() => new Response(null, { status: 204 }));

    await h.client.logoutAll();

    expect(h.calls.map((c) => [c.path, c.csrf])).toEqual([["/api/auth/session/logout-all", "csrf-1"]]);
    expect(h.client.getState()).toEqual({ status: "anonymous" });
  });

  it("switching waits for a refresh in flight instead of joining it", async () => {
    const h = harness();
    await signedIn(h, "token-1");
    const refreshAnswer = deferred<Response>();
    h.serve((call) => {
      if (call.path.endsWith("/refresh")) return refreshAnswer.promise;
      if (call.path.endsWith("/switch-institution")) return sessionBody("token-switched", h.clock.now);
      return json(401, {});
    });

    const pending = h.client.fetch("/api/x"); // 401, starts a refresh that is held open
    await vi.waitFor(() => expect(h.refreshes()).toHaveLength(1));
    const switched = h.client.switchInstitution("colombo-kovil");
    refreshAnswer.resolve(sessionBody("token-2", h.clock.now));
    await pending;

    expect(await switched).toEqual({ ok: true });
    expect(h.to("/api/auth/session/switch-institution")).toHaveLength(1);
    expect(h.client.getState()).toMatchObject({ accessToken: "token-switched" });
  });

  it("switching to the personal account rotates through its own endpoint, with the CSRF header", async () => {
    const h = harness();
    await signedIn(h, "token-1");
    h.serve((call) =>
      call.path.endsWith("/switch-personal") ? sessionBody("token-personal", h.clock.now) : json(404, {}),
    );

    expect(await h.client.switchToPersonal()).toEqual({ ok: true });
    expect(h.calls.map((c) => [c.path, c.csrf])).toEqual([["/api/auth/session/switch-personal", "csrf-1"]]);
    expect(h.client.getState()).toMatchObject({ accessToken: "token-personal" });
  });

  it("a refused switch keeps the session", async () => {
    const h = harness();
    await signedIn(h, "token-1");
    h.serve(() => json(403, { code: "institution_not_available" }));

    expect(await h.client.switchInstitution("not-mine")).toEqual({ ok: false, reason: "not_available" });
    expect(h.client.getState()).toMatchObject({ status: "authenticated", accessToken: "token-1" });
  });
});
