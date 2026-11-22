import type { ApiFailure } from "./api";

/*  The step-up form's decision after asking for a code, kept out of the component so it can be
    tested without a DOM.                                                                     */

export type AfterStepUpSend = {
  /** Seconds before "Send a new code" may be pressed, or null to leave the countdown alone. */
  wait: number | null;
  /** Show the failure above the form. False when the code already texted is still good. */
  showError: boolean;
  /** No code can ever arrive (no confirmed phone, account not active): only cancel is offered. */
  blocked: boolean;
};

/**
 * The form asks for a code as it opens, so cancelling and opening it again within the minute asks
 * again inside the resend cooldown. The API then says `code_already_sent`: the earlier code still
 * works, so the form says "code sent" and counts down, with no error (QA DEF-003). Any other 429 -
 * the hourly cap - is a real refusal, shown, with its wait.
 */
export function afterStepUpSendFailure(failure: ApiFailure): AfterStepUpSend {
  const wait = failure.status === 429 && failure.retryAfter ? failure.retryAfter : null;
  if (failure.code === "code_already_sent") return { wait, showError: false, blocked: false };
  const blocked = failure.code === "no_verified_phone" || failure.code === "account_not_active";
  return { wait, showError: true, blocked };
}
