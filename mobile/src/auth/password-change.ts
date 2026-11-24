/*  The change-password form's logic (F39), free of React: what the phone checks before sending,
    and how each answer of POST /api/auth/mobile/password lands on the form. The API decides; these
    checks only save a round trip for what the donor can fix. A port of
    web/lib/auth/password-change.ts.                                                            */

export type PasswordChangeValues = { current: string; next: string; confirm: string };

export type PasswordChangeField = keyof PasswordChangeValues;

export type PasswordChangeProblem =
  | { kind: "required" }
  | { kind: "incorrect" }
  | { kind: "unchanged" }
  | { kind: "mismatch" }
  | { kind: "rejected"; rules: string[] };

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
  /** Whether the message goes above the form (account not active, too many tries) because no field says it. */
  formFailure: boolean;
};

export function applyPasswordChangeFailure(
  values: PasswordChangeValues,
  failure: { code: string | null; rules: string[] },
): PasswordChangeOutcome {
  switch (failure.code) {
    case "password_incorrect":
      // Retyped, not edited blind - and a locked account answers this too, so the same advice holds.
      return { values: { ...values, current: "" }, errors: { current: { kind: "incorrect" } }, formFailure: false };
    case "password_unchanged":
      return { values, errors: { next: { kind: "unchanged" } }, formFailure: false };
    case "password_rejected":
      // The confirmation matched a password that will not be used; it must be typed again with the next one.
      return { values: { ...values, confirm: "" }, errors: { next: { kind: "rejected", rules: failure.rules } }, formFailure: false };
    default:
      return { values, errors: {}, formFailure: true };
  }
}
