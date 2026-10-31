import type { ApiFailure, PasswordRule } from "./api";

/*  The form logic of /account/password, kept free of React so it can be unit-tested: what the
    browser checks before sending, and how each answer of POST /api/me/password lands on the
    form. The API decides; these checks only save a round trip for what the user can fix.     */

export type PasswordChangeValues = { current: string; next: string; confirm: string };

export type PasswordChangeField = keyof PasswordChangeValues;

export type PasswordChangeProblem =
  | { kind: "required" }
  | { kind: "incorrect" }
  | { kind: "unchanged" }
  | { kind: "mismatch" }
  | { kind: "rejected"; rules: PasswordRule[] };

export type PasswordChangeErrors = Partial<Record<PasswordChangeField, PasswordChangeProblem>>;

export const emptyPasswordChange: PasswordChangeValues = { current: "", next: "", confirm: "" };

/** What can be told before sending. Empty means the form may be submitted. */
export function checkPasswordChange(values: PasswordChangeValues): PasswordChangeErrors {
  const errors: PasswordChangeErrors = {};
  if (!values.current) errors.current = { kind: "required" };
  if (!values.next) errors.next = { kind: "required" };
  else if (values.next === values.current) errors.next = { kind: "unchanged" };
  if (!values.confirm) errors.confirm = { kind: "required" };
  else if (values.next && values.confirm !== values.next) errors.confirm = { kind: "mismatch" };
  return errors;
}

/** The form after the API refused it: field errors where a field can fix it, otherwise a form-level failure. */
export type PasswordChangeOutcome = {
  values: PasswordChangeValues;
  errors: PasswordChangeErrors;
  /** Shown above the form (account not active, too many tries, offline); null when a field says it. */
  formFailure: ApiFailure | null;
};

export function applyPasswordChangeFailure(values: PasswordChangeValues, failure: ApiFailure): PasswordChangeOutcome {
  switch (failure.code) {
    case "password_incorrect":
      // Retyped, not edited blind - and a locked account answers this too, so the same advice holds.
      return { values: { ...values, current: "" }, errors: { current: { kind: "incorrect" } }, formFailure: null };
    case "password_unchanged":
      return { values, errors: { next: { kind: "unchanged" } }, formFailure: null };
    case "password_rejected":
      // The confirmation matched a password that will not be used; it must be typed again with the next one.
      return {
        values: { ...values, confirm: "" },
        errors: { next: { kind: "rejected", rules: failure.passwordFailures } },
        formFailure: null,
      };
    default:
      return { values, errors: {}, formFailure: failure };
  }
}
