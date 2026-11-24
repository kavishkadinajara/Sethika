import { router } from "expo-router";

import SignInScreen from "@/app/(auth)/sign-in";
import { I18nProvider } from "@/i18n/i18n-provider";
import { press, render } from "@/test-utils/render";
import { ThemePreferenceProvider } from "@/theme/appearance";

/*  Only what F38 added to the sign-in screen: the way to the reset and the message on the way back. */

let mockParams: { next?: string; reset?: string } = {};

jest.mock("@/auth/session-provider", () => ({ useSessionClient: () => ({ establish: jest.fn() }) }));
jest.mock("expo-router", () => ({
  router: { push: jest.fn(), back: jest.fn(), dismissTo: jest.fn() },
  useLocalSearchParams: () => mockParams,
}));

const page = () => (
  <ThemePreferenceProvider>
    <I18nProvider>
      <SignInScreen />
    </I18nProvider>
  </ThemePreferenceProvider>
);

beforeEach(() => {
  mockParams = {};
  jest.mocked(router.push).mockClear();
});

it("opens the reset from the link", async () => {
  const screen = await render(page());

  await press(screen.getText("Forgot password?"));

  expect(router.push).toHaveBeenCalledWith({ pathname: "/forgot-password", params: {} });
});

it("hands on where the donor was going", async () => {
  mockParams = { next: "/i/kandy-temple" };
  const screen = await render(page());

  await press(screen.getText("Forgot password?"));

  expect(router.push).toHaveBeenCalledWith({ pathname: "/forgot-password", params: { next: "/i/kandy-temple" } });
});

it("says the password was changed when the reset sent the donor back", async () => {
  mockParams = { reset: "1" };

  const screen = await render(page());

  screen.getText("Your password was changed");
  screen.getText("Sign in with your new password.");
});

it("says nothing of the kind on an ordinary sign-in", async () => {
  const screen = await render(page());

  expect(screen.queryText("Your password was changed")).toBeNull();
});
