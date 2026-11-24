import { applyPasswordChangeFailure, checkPasswordChange, emptyPasswordChange } from "./password-change";

const filled = { current: "Old#Pass2026", next: "New#Pass2027", confirm: "New#Pass2027" };

describe("checkPasswordChange", () => {
  it("lets a complete form through", () => {
    expect(checkPasswordChange(filled)).toEqual({});
  });

  it("asks for every field", () => {
    expect(checkPasswordChange(emptyPasswordChange)).toEqual({
      current: { kind: "required" },
      next: { kind: "required" },
      confirm: { kind: "required" },
    });
  });

  it("refuses the current password as the new one, before asking the API", () => {
    expect(checkPasswordChange({ ...filled, next: filled.current, confirm: filled.current })).toEqual({ next: { kind: "unchanged" } });
  });

  it("catches a confirmation that does not match", () => {
    expect(checkPasswordChange({ ...filled, confirm: "Other#Pass1" })).toEqual({ confirm: { kind: "mismatch" } });
  });

  it("does not call a confirmation a mismatch while the new password is missing", () => {
    expect(checkPasswordChange({ ...filled, next: "" })).toEqual({ next: { kind: "required" } });
  });
});

describe("applyPasswordChangeFailure", () => {
  it("a wrong current password is retyped, and nothing else is touched", () => {
    expect(applyPasswordChangeFailure(filled, { code: "password_incorrect", rules: [] })).toEqual({
      values: { ...filled, current: "" },
      errors: { current: { kind: "incorrect" } },
      formFailure: false,
    });
  });

  it("an unchanged password lands on the new password's field", () => {
    expect(applyPasswordChangeFailure(filled, { code: "password_unchanged", rules: [] })).toEqual({
      values: filled,
      errors: { next: { kind: "unchanged" } },
      formFailure: false,
    });
  });

  it("a rejected password lists its rules and asks for the confirmation again", () => {
    expect(applyPasswordChangeFailure(filled, { code: "password_rejected", rules: ["RecentlyUsed"] })).toEqual({
      values: { ...filled, confirm: "" },
      errors: { next: { kind: "rejected", rules: ["RecentlyUsed"] } },
      formFailure: false,
    });
  });

  it.each(["account_not_active", "too_many_requests", null])("anything else goes above the form (%s)", (code) => {
    expect(applyPasswordChangeFailure(filled, { code, rules: [] })).toEqual({ values: filled, errors: {}, formFailure: true });
  });
});
