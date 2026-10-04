import type { ApiFailure } from "./api";

/*  The forgotten-password flow's decisions, kept out of the component so they can be tested
    without a DOM: which step a failure leaves the user on, where its message goes, and when
    the reset ticket is no longer worth keeping.                                            */

export type ResetStep = "phone" | "code" | "password";
export const resetSteps: readonly ResetStep[] = ["phone", "code", "password"];

/** Where a message is shown: under one field, or above the whole form. */
export type MessagePlace = "phone" | "code" | "password" | "confirmation" | "form";

/** After asking for a code. Only a malformed number is the field's fault. */
export function afterRequestFailure(failure: ApiFailure): MessagePlace {
  return failure.code === "invalid_phone_number" ? "phone" : "form";
}

/**
 * After a code check fails the user stays on the code step: a wrong, expired or burned code is
 * the code field's problem (send a new one), anything else - 429, the API down - the form's.
 * The typed code is always cleared, so the next try starts empty.
 */
export function afterVerifyFailure(failure: ApiFailure): { step: ResetStep; place: MessagePlace } {
  return { step: "code", place: failure.status === 400 ? "code" : "form" };
}

/**
 * After the new password is refused. A rejected password is fixed and resubmitted with the same
 * ticket (the API does not spend it); an invalid ticket - expired, used, or the account changed -
 * means verifying the number again.
 */
export function afterResetFailure(failure: ApiFailure): { step: ResetStep; place: MessagePlace; keepTicket: boolean } {
  switch (failure.code) {
    case "password_rejected":
      return { step: "password", place: "password", keepTicket: true };
    case "reset_ticket_invalid":
      return { step: "phone", place: "form", keepTicket: false };
    default:
      return { step: "password", place: "form", keepTicket: true };
  }
}

/** The checks made before the new password is sent. The policy itself is the server's. */
export type NewPasswordProblems = { password?: "required"; confirmation?: "required" | "mismatch" };

export function checkNewPassword(password: string, confirmation: string): NewPasswordProblems {
  const problems: NewPasswordProblems = {};
  if (!password) problems.password = "required";
  if (!confirmation) problems.confirmation = "required";
  else if (password && confirmation !== password) problems.confirmation = "mismatch";
  return problems;
}
