import { describe, expect, it } from "vitest";
import type { ApiFailure } from "./api";
import { applyPasswordChangeFailure, checkPasswordChange, type PasswordChangeValues } from "./password-change";

const filled: PasswordChangeValues = { current: "Old-horse-1", next: "New-horse-2", confirm: "New-horse-2" };

function failure(status: number, code: string | null, extra: Partial<ApiFailure> = {}): ApiFailure {
  return { status, code, retryAfter: null, attemptsRemaining: null, passwordFailures: [], ...extra };
}

describe("checkPasswordChange (before sending)", () => {
  it("a complete form with a matching confirmation has nothing to say", () => {
    expect(checkPasswordChange(filled)).toEqual({});
  });

  it("asks for every empty field at once", () => {
    expect(checkPasswordChange({ current: "", next: "", confirm: "" })).toEqual({
      current: { kind: "required" },
      next: { kind: "required" },
      confirm: { kind: "required" },
    });
  });

  it("a confirmation that differs from the new password is a mismatch on the confirmation", () => {
    expect(checkPasswordChange({ ...filled, confirm: "New-horse-3" })).toEqual({ confirm: { kind: "mismatch" } });
  });

  it("the current password typed again as the new one is unchanged, without a round trip", () => {
    expect(checkPasswordChange({ current: "Same-horse-1", next: "Same-horse-1", confirm: "Same-horse-1" })).toEqual({
      next: { kind: "unchanged" },
    });
  });

  it("does not call an empty new password a mismatch: the field that is empty says so", () => {
    expect(checkPasswordChange({ ...filled, next: "" })).toEqual({ next: { kind: "required" } });
  });
});

describe("applyPasswordChangeFailure (the API's answer)", () => {
  it("password_incorrect lands on the current password, which is cleared to be retyped", () => {
    const outcome = applyPasswordChangeFailure(filled, failure(400, "password_incorrect"));
    expect(outcome.errors).toEqual({ current: { kind: "incorrect" } });
    expect(outcome.values).toEqual({ ...filled, current: "" });
    expect(outcome.formFailure).toBeNull();
  });

  it("password_unchanged lands on the new password", () => {
    const outcome = applyPasswordChangeFailure(filled, failure(400, "password_unchanged"));
    expect(outcome.errors).toEqual({ next: { kind: "unchanged" } });
    expect(outcome.values).toEqual(filled);
  });

  it("password_rejected lists the broken rules on the new password and clears the confirmation", () => {
    const outcome = applyPasswordChangeFailure(
      filled,
      failure(400, "password_rejected", { passwordFailures: ["TooShort", "ContainsPersonalData"] }),
    );
    expect(outcome.errors).toEqual({ next: { kind: "rejected", rules: ["TooShort", "ContainsPersonalData"] } });
    expect(outcome.values).toEqual({ ...filled, confirm: "" });
    expect(outcome.formFailure).toBeNull();
  });

  it.each([
    ["too many tries", failure(429, "too_many_requests", { retryAfter: 600 })],
    ["account not active", failure(403, "account_not_active")],
    ["offline", failure(0, null)],
  ])("%s is a form-level failure, and no field is blamed or cleared", (_, apiFailure) => {
    const outcome = applyPasswordChangeFailure(filled, apiFailure);
    expect(outcome.errors).toEqual({});
    expect(outcome.values).toEqual(filled);
    expect(outcome.formFailure).toBe(apiFailure);
  });
});
