import { afterEach, describe, expect, it, vi } from "vitest";
import { logIn, readFailure, requestSignInCode, signInWithCode } from "./api";
import { session } from "./index";
import { safeNextPath } from "./next-path";

function json(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("safeNextPath (open-redirect guard)", () => {
  it.each([
    ["/select-institution", "/select-institution"],
    ["/bookings?date=2026-10-01", "/bookings?date=2026-10-01"],
    [null, "/"],
    ["", "/"],
    ["//evil.example", "/"],
    ["https://evil.example", "/"],
    ["/\\evil.example", "/"],
    ["/api/auth/session/logout", "/"],
    ["/ok\nSet-Cookie:x", "/"],
  ])("%j -> %j", (raw, expected) => {
    expect(safeNextPath(raw)).toBe(expected);
  });
});

describe("readFailure", () => {
  it("reads password_rejected's failures and drops names it does not know", async () => {
    const failure = await readFailure(
      json(400, { code: "password_rejected", failures: ["TooShort", "MissingDigit", "SomethingNew"] }),
    );
    expect(failure).toMatchObject({ status: 400, code: "password_rejected", passwordFailures: ["TooShort", "MissingDigit"] });
  });

  it("reads Retry-After on a 429", async () => {
    const failure = await readFailure(json(429, { code: "too_many_requests" }, { "Retry-After": "75" }));
    expect(failure).toMatchObject({ status: 429, code: "too_many_requests", retryAfter: 75 });
  });

  it("reads attemptsRemaining from otp_incorrect", async () => {
    const failure = await readFailure(json(400, { code: "otp_incorrect", attemptsRemaining: 2 }));
    expect(failure).toMatchObject({ code: "otp_incorrect", attemptsRemaining: 2 });
  });

  it("copes with a body that is not JSON", async () => {
    const failure = await readFailure(new Response("Bad gateway", { status: 502 }));
    expect(failure).toMatchObject({ status: 502, code: null, retryAfter: null });
  });
});

describe("logIn", () => {
  it("a 401 invalid_credentials is a wrong password, not an expired session: no refresh", async () => {
    const fetchMock = vi.fn(async () => json(401, { code: "invalid_credentials" }));
    vi.stubGlobal("fetch", fetchMock);
    // A session cookie is present, so a wrongly routed call would have something to refresh with.
    vi.stubGlobal("document", { cookie: "sethika-csrf=csrf-1" });

    const result = await logIn("0771234567", "wrong");

    expect(result).toMatchObject({ ok: false, failure: { status: 401, code: "invalid_credentials" } });
    expect(fetchMock.mock.calls.map((call) => String((call as unknown[])[0]))).toEqual(["/api/auth/login"]);
  });

  it("a 200 starts the session from the response body", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        json(200, {
          accessToken: "token-from-login",
          accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
          institutions: [],
          activeInstitution: null,
        }),
      ),
    );

    const result = await logIn("0771234567", "Correct-horse-1");

    expect(result.ok).toBe(true);
    expect(session.getState()).toMatchObject({ status: "authenticated", accessToken: "token-from-login" });
  });
});

describe("sign-in by code", () => {
  it("asks for a code with the login purpose, not signup's", async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => new Response(null, { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await requestSignInCode("0771234567");

    expect(result.ok).toBe(true);
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/auth/otp/request");
    expect(JSON.parse(String(init?.body))).toEqual({ phoneNumber: "0771234567", purpose: "login" });
  });

  it("a 401 otp_invalid is a wrong code, not an expired session: no refresh, no session", async () => {
    const fetchMock = vi.fn(async () => json(401, { code: "otp_invalid" }));
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("document", { cookie: "sethika-csrf=csrf-1" });
    const before = session.getState();

    const result = await signInWithCode("0771234567", "123456");

    expect(result).toMatchObject({ ok: false, failure: { status: 401, code: "otp_invalid" } });
    expect(fetchMock.mock.calls.map((call) => String((call as unknown[])[0]))).toEqual(["/api/auth/otp/sign-in"]);
    expect(session.getState()).toEqual(before);
  });

  it("a 200 starts the session from the response body, as password login does", async () => {
    const fetchMock = vi.fn<typeof fetch>(async () =>
      json(200, {
        accessToken: "token-from-code",
        accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        institutions: [],
        activeInstitution: null,
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const result = await signInWithCode("0771234567", "123456");

    expect(result.ok).toBe(true);
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ phoneNumber: "0771234567", code: "123456" });
    expect(session.getState()).toMatchObject({ status: "authenticated", accessToken: "token-from-code" });
  });
});
