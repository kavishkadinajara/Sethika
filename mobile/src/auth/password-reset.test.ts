import { afterRequestFailure, afterResetFailure, afterVerifyFailure, checkNewPassword, type ResetFailure } from "./password-reset";

/*  The same cases as web/lib/auth/password-reset.test.ts: the two ports must decide alike.  */

const failure = (status: number, code: string | null): ResetFailure => ({ status, code });

describe("afterRequestFailure", () => {
  it("puts a malformed number under the phone field", () => {
    expect(afterRequestFailure(failure(400, "invalid_phone_number"))).toBe("phone");
  });

  it.each([failure(429, "too_many_requests"), failure(503, "verification_unavailable"), failure(0, null)])(
    "puts anything else above the form (%j)",
    (f) => {
      expect(afterRequestFailure(f)).toBe("form");
    },
  );
});

describe("afterVerifyFailure", () => {
  it.each(["otp_incorrect", "otp_not_found", "otp_attempts_exhausted"])("keeps a %s on the code step, under the code field", (code) => {
    expect(afterVerifyFailure(failure(400, code))).toEqual({ step: "code", place: "code" });
  });

  it("keeps a 429, an outage or no answer on the code step, above the form", () => {
    expect(afterVerifyFailure(failure(429, "too_many_requests"))).toEqual({ step: "code", place: "form" });
    expect(afterVerifyFailure(failure(503, "verification_unavailable"))).toEqual({ step: "code", place: "form" });
    expect(afterVerifyFailure(failure(0, null))).toEqual({ step: "code", place: "form" });
  });
});

describe("afterResetFailure", () => {
  it("a rejected password is fixed on the same step, with the same ticket", () => {
    expect(afterResetFailure(failure(400, "password_rejected"))).toEqual({ step: "password", place: "password", keepTicket: true });
  });

  it("an invalid ticket sends the donor back to verify the number, and forgets the ticket", () => {
    expect(afterResetFailure(failure(400, "reset_ticket_invalid"))).toEqual({ step: "phone", place: "form", keepTicket: false });
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
