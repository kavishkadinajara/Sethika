import { router } from "expo-router";
import { act } from "react";

import type { ChangePasswordResult, SessionState } from "@/auth/session-client";
import { I18nProvider } from "@/i18n/i18n-provider";
import { ANONYMOUS, DONOR } from "@/test-utils/api-stub";
import { json, press, render, settle, type, type Screen } from "@/test-utils/render";
import { ThemePreferenceProvider } from "@/theme/appearance";

import { ChangePasswordForm } from "./change-password-form";

/*  F39's form against the session client. What happens to the tokens is session-client.test.ts's. */

let mockSession: SessionState = DONOR;
const mockChange = jest.fn<Promise<ChangePasswordResult>, [string, string]>();

jest.mock("@/auth/session-provider", () => ({
  useSession: () => mockSession,
  useSessionClient: () => ({ changePassword: mockChange }),
}));
jest.mock("expo-router", () => ({ router: { push: jest.fn(), dismissTo: jest.fn() } }));

const CURRENT = "Dana!2026Sethika";
const NEXT = "Pirith#Night2027";

const page = () => (
  <ThemePreferenceProvider>
    <I18nProvider>
      <ChangePasswordForm />
    </I18nProvider>
  </ThemePreferenceProvider>
);

const refusal = (status: number, body: unknown): ChangePasswordResult => ({ ok: false, response: json(status, body) });
const alertOn = (screen: Screen) => screen.where((props) => props.accessibilityRole === "alert").length > 0;
const value = (screen: Screen, label: string) => screen.getLabel(label).props.value as string;

async function fill(screen: Screen, current = CURRENT, next = NEXT, confirm = next) {
  await type(screen.getLabel("Current password"), current);
  await type(screen.getLabel("New password"), next);
  await type(screen.getLabel("Confirm new password"), confirm);
}

const submit = (screen: Screen) => press(screen.getText("Change password"));

beforeEach(() => {
  mockSession = DONOR;
  mockChange.mockReset();
  mockChange.mockResolvedValue({ ok: true });
  jest.mocked(router.push).mockClear();
  jest.mocked(router.dismissTo).mockClear();
});

it("sends the two passwords, then says so and shows no form", async () => {
  const screen = await render(page());
  await fill(screen);

  await submit(screen);

  expect(mockChange).toHaveBeenCalledWith(CURRENT, NEXT);
  screen.getText("Password changed");
  screen.getText("Your other devices have been signed out.");
  expect(screen.queryLabel("Current password")).toBeNull();

  await press(screen.getText("Go to the home page"));
  expect(router.dismissTo).toHaveBeenCalledWith("/");
});

it("still says changed when the phone lost the session on the way", async () => {
  // session-client signs out when the new token cannot be stored, but the password did change.
  mockChange.mockImplementation(async () => {
    mockSession = ANONYMOUS;
    return { ok: true };
  });
  const screen = await render(page());
  await fill(screen);

  await submit(screen);

  screen.getText("Password changed");
});

it("asks for every field before calling the API", async () => {
  const screen = await render(page());

  await submit(screen);

  screen.getText("Enter your current password.");
  screen.getText("Choose a new password.");
  screen.getText("Type your new password again to confirm it.");
  expect(mockChange).not.toHaveBeenCalled();
});

it("catches a mismatch and an unchanged password before calling the API", async () => {
  const screen = await render(page());

  await fill(screen, CURRENT, NEXT, "Something#Else1");
  await submit(screen);
  screen.getText("This doesn't match the new password.");

  await fill(screen, CURRENT, CURRENT);
  await submit(screen);
  screen.getText("Choose a password that is different from your current one.");

  expect(mockChange).not.toHaveBeenCalled();
});

it("a wrong current password is said at that field, and only it is emptied", async () => {
  mockChange.mockResolvedValue(refusal(400, { code: "password_incorrect" }));
  const screen = await render(page());
  await fill(screen);

  await submit(screen);

  screen.getText("That isn't your current password.");
  expect(alertOn(screen)).toBe(false);
  expect(value(screen, "Current password")).toBe("");
  expect(value(screen, "New password")).toBe(NEXT);
  expect(value(screen, "Confirm new password")).toBe(NEXT);
});

it("an unchanged password the API caught is said at the new password", async () => {
  mockChange.mockResolvedValue(refusal(400, { code: "password_unchanged" }));
  const screen = await render(page());
  await fill(screen);

  await submit(screen);

  screen.getText("Choose a password that is different from your current one.");
  expect(alertOn(screen)).toBe(false);
});

it("a refused password says which rules, and asks for the confirmation again", async () => {
  mockChange.mockResolvedValue(refusal(400, { code: "password_rejected", failures: ["RecentlyUsed"] }));
  const screen = await render(page());
  await fill(screen);

  await submit(screen);

  screen.getText("Please choose a stronger password.");
  screen.getText("• Not a password you used recently");
  expect(value(screen, "Confirm new password")).toBe("");
  expect(value(screen, "Current password")).toBe(CURRENT);
});

it.each([
  ["a closed account", 403, { code: "account_not_active" }, "This account is not active. Please contact Sethika support."],
  ["too many tries", 429, { code: "too_many_requests" }, "Too many tries. Please wait a minute and try again."],
])("%s goes above the form and keeps what was typed", async (_name, status, body, words) => {
  mockChange.mockResolvedValue(refusal(status, body));
  const screen = await render(page());
  await fill(screen);

  await submit(screen);

  screen.getText(words);
  expect(alertOn(screen)).toBe(true);
  expect(value(screen, "Current password")).toBe(CURRENT);
});

it("no answer says so, and the same press tries again", async () => {
  mockChange.mockResolvedValueOnce({ ok: false, response: null });
  const screen = await render(page());
  await fill(screen);

  await submit(screen);
  screen.getText("Can't reach Sethika right now. Check your connection and try again.");

  await submit(screen);
  screen.getText("Password changed");
});

it("a message above the form goes with the next try", async () => {
  mockChange.mockResolvedValueOnce({ ok: false, response: null });
  const screen = await render(page());
  await fill(screen);
  await submit(screen);

  await type(screen.getLabel("Current password"), "");
  await submit(screen);

  expect(screen.queryText("Can't reach Sethika right now. Check your connection and try again.")).toBeNull();
});

it("the keyboard's key sends once while a change is out", async () => {
  let release: (result: ChangePasswordResult) => void = () => {};
  mockChange.mockReturnValue(new Promise((resolve) => (release = resolve)));
  const screen = await render(page());
  await fill(screen);

  for (let i = 0; i < 2; i++) {
    await act(async () => {
      screen.getLabel("Confirm new password").props.onSubmitEditing();
    });
  }
  release({ ok: true });
  await settle();

  expect(mockChange).toHaveBeenCalledTimes(1);
});

it("a visitor is asked to sign in, and sees no form", async () => {
  mockSession = ANONYMOUS;

  const screen = await render(page());

  screen.getText("Sign in to change your password.");
  expect(screen.queryLabel("Current password")).toBeNull();

  await press(screen.getText("Sign in"));
  expect(router.push).toHaveBeenCalledWith("/sign-in");
});
