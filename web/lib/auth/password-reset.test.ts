import { afterEach, describe, expect, it, vi } from "vitest";
import { requestResetCode, resetPassword, verifyResetCode, type ApiFailure } from "./api";
import { session } from "./index";
import { afterRequestFailure, afterResetFailure, afterVerifyFailure, checkNewPassword } from "./password-reset";

function failure(status: number, code: string | null, extra: Partial<ApiFailure> = {}): ApiFailure {
  return { status, code, retryAfter: null, attemptsRemaining: null, passwordFailures: [], ...extra };
}

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

/** The JSON body of the n-th fetch call. */
function bodyOf(fetchMock: ReturnType<typeof vi.fn>, n = 0) {
  const init = (fetchMock.mock.calls[n] as unknown[])[1] as RequestInit;
  return JSON.parse(String(init.body)) as Record<string, unknown>;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("afterRequestFailure", () => {
  it("puts a malformed number under the phone field", () => {
    expect(afterRequestFailure(failure(400, "invalid_phone_number"))).toBe("phone");
  });

  it.each([
    failure(429, "too_many_requests", { retryAfter: 60 }),
    failure(503, "verification_unavailable"),
    failure(0, null),
  ])("puts anything else above the form (%j)", (f) => {
    expect(afterRequestFailure(f)).toBe("form");
  });
});

describe("afterVerifyFailure", () => {
  it.each(["otp_incorrect", "otp_not_found", "otp_attempts_exhausted"])(
    "keeps a %s on the code step, under the code field",
    (code) => {
      expect(afterVerifyFailure(failure(400, code))).toEqual({ step: "code", place: "code" });
    },
  );

  it("keeps a 429 or an outage on the code step, above the form", () => {
    expect(afterVerifyFailure(failure(429, "too_many_requests"))).toEqual({ step: "code", place: "form" });
    expect(afterVerifyFailure(failure(503, "verification_unavailable"))).toEqual({ step: "code", place: "form" });
  });
});

describe("afterResetFailure", () => {
  it("a rejected password is fixed on the same step, with the same ticket", () => {
    expect(afterResetFailure(failure(400, "password_rejected", { passwordFailures: ["TooShort"] }))).toEqual({
      step: "password",
      place: "password",
      keepTicket: true,
    });
  });

  it("an invalid ticket sends the user back to verify the number, and forgets the ticket", () => {
    expect(afterResetFailure(failure(400, "reset_ticket_invalid"))).toEqual({
      step: "phone",
      place: "form",
      keepTicket: false,
    });
  });

  it.each([failure(429, "too_many_requests"), failure(503, "verification_unavailable"), failure(0, null)])(
    "anything else is retried from the password step with the same ticket (%j)",
    (f) => {
      expect(afterResetFailure(f)).toEqual({ step: "password", place: "form", keepTicket: true });
    },
  );
});

describe("checkNewPassword", () => {
  it("accepts two matching passwords and leaves the policy to the server", () => {
    expect(checkNewPassword("weak", "weak")).toEqual({});
  });

  it("asks for both fields", () => {
    expect(checkNewPassword("", "")).toEqual({ password: "required", confirmation: "required" });
  });

  it("catches a confirmation that does not match", () => {
    expect(checkNewPassword("Lotus#2027", "Lotus#2028")).toEqual({ confirmation: "mismatch" });
  });

  it("does not call an empty password a mismatch: it is missing", () => {
    expect(checkNewPassword("", "Lotus#2027")).toEqual({ password: "required" });
  });
});

describe("the reset calls", () => {
  it("asks for a code with the reset purpose", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await requestResetCode("0771234567", "si");

    expect(result.ok).toBe(true);
    expect(String((fetchMock.mock.calls[0] as unknown[])[0])).toBe("/api/auth/otp/request");
    expect(bodyOf(fetchMock)).toEqual({ phoneNumber: "0771234567", purpose: "reset", language: "si" });
  });

  it("verifies with the reset purpose and returns the ticket", async () => {
    const fetchMock = vi.fn(async () => json(200, { ticket: "reset-ticket" }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await verifyResetCode("0771234567", "123456");

    expect(result).toEqual({ ok: true, value: "reset-ticket" });
    expect(bodyOf(fetchMock)).toEqual({ phoneNumber: "0771234567", purpose: "reset", code: "123456" });
  });

  it("a 204 reset starts no session", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    const before = session.getState().status;

    const result = await resetPassword("reset-ticket", "Lotus#2027Offering");

    expect(result.ok).toBe(true);
    expect(session.getState().status).toBe(before);
    expect(String((fetchMock.mock.calls[0] as unknown[])[0])).toBe("/api/auth/password/reset");
    expect(bodyOf(fetchMock)).toEqual({ ticket: "reset-ticket", newPassword: "Lotus#2027Offering" });
  });

  it("reports an invalid ticket as a failure the screen can map", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json(400, { code: "reset_ticket_invalid" })));

    const result = await resetPassword("spent", "Lotus#2027Offering");

    expect(result).toMatchObject({ ok: false, failure: { status: 400, code: "reset_ticket_invalid" } });
  });
});
