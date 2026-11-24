import { describe, expect, it } from "vitest";
import type { ApiFailure } from "./api";
import { afterStepUpSendFailure } from "./step-up";

function failure(status: number, code: string | null, extra: Partial<ApiFailure> = {}): ApiFailure {
  return { status, code, retryAfter: null, attemptsRemaining: null, passwordFailures: [], ...extra };
}

describe("afterStepUpSendFailure", () => {
  it("asked again within the minute, the earlier code still works: a countdown and no error (QA DEF-003)", () => {
    expect(afterStepUpSendFailure(failure(429, "code_already_sent", { retryAfter: 46 }))).toEqual({
      wait: 46,
      showError: false,
      blocked: false,
    });
  });

  it("the hourly cap is a real refusal, shown with its wait", () => {
    expect(afterStepUpSendFailure(failure(429, "too_many_requests", { retryAfter: 1800 }))).toEqual({
      wait: 1800,
      showError: true,
      blocked: false,
    });
  });

  it.each(["no_verified_phone", "account_not_active"])("%s blocks the form: no code can ever arrive", (code) => {
    expect(afterStepUpSendFailure(failure(403, code))).toEqual({ wait: null, showError: true, blocked: true });
  });

  it.each([failure(503, "verification_unavailable"), failure(0, null)])("anything else is shown and the form stays open", (f) => {
    expect(afterStepUpSendFailure(f)).toEqual({ wait: null, showError: true, blocked: false });
  });
});
