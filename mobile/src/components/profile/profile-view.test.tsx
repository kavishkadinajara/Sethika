import { router } from "expo-router";
import { act } from "react";
import { Appearance, AppState, Linking } from "react-native";

import type { SessionState, SwitchInstitutionResult } from "@/auth/session-client";
import { ProfileProvider } from "@/auth/profile-provider";
import { I18nProvider } from "@/i18n/i18n-provider";
import { PushProvider } from "@/notifications/push-provider";
import { ANONYMOUS, createApiStub, DONOR, STAFF } from "@/test-utils/api-stub";
import { booking } from "@/test-utils/fixtures";
import { CHOICE_KEY, createPushStub, permissions, TOKEN, TOKEN_KEY } from "@/test-utils/push-stub";
import { json, press, render, settle, type Screen } from "@/test-utils/render";
import { ThemePreferenceProvider } from "@/theme/appearance";

import { ProfileView } from "./profile-view";

/*  The Profile tab against GET /api/me and GET /api/bookings/mine, stubbed at the app's client
    boundary, inside the real profile, language and theme providers. Today is 2026-11-10.     */

const mockApi = createApiStub();
const mockPhone = createPushStub();
let mockSession: SessionState = DONOR;

/** The session client, with the two sign-outs leaving their mark among the API calls. */
const mockClient = {
  ...mockApi.client,
  getState: () => mockSession,
  logout: jest.fn(async () => void mockApi.sent.push({ method: "END", path: "logout", body: null, signedIn: true })),
  logoutAll: jest.fn(async () => void mockApi.sent.push({ method: "END", path: "logout-all", body: null, signedIn: true })),
  switchInstitution: jest.fn<Promise<SwitchInstitutionResult>, [string]>(),
  switchToPersonal: jest.fn<Promise<SwitchInstitutionResult>, []>(),
};

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockClient }));
jest.mock("@/notifications/native-device", () => ({
  get nativeDevice() {
    return mockPhone.device;
  },
  get nativeMemory() {
    return mockPhone.memory;
  },
}));
jest.mock("expo-notifications", () => ({
  useLastNotificationResponse: () => null,
  clearLastNotificationResponseAsync: async () => {},
}));
jest.mock("expo-router", () => ({
  router: { push: jest.fn(), navigate: jest.fn(), dismissTo: jest.fn() },
  useFocusEffect: (effect: () => void) => jest.requireActual<typeof import("react")>("react").useEffect(effect, [effect]),
}));

const MINE = [
  booking({ bookingNo: "BK-2026-000001", date: "2026-11-30", status: "Approved", headcount: 40 }),
  booking({ bookingNo: "BK-2026-000002", date: "2026-11-01", status: "Completed", headcount: 25 }),
  booking({ bookingNo: "BK-2026-000003", date: "2026-11-12", status: "Requested", headcount: 10, slotName: "Dinner" }),
  booking({ bookingNo: "BK-2026-000004", date: "2026-10-20", status: "Completed", headcount: 15 }),
  booking({ bookingNo: "BK-2026-000005", date: "2026-11-05", status: "NoShow", headcount: 30 }),
];

const page = () => (
  <ThemePreferenceProvider>
    <I18nProvider>
      <ProfileProvider>
        <PushProvider>
          <ProfileView />
        </PushProvider>
      </ProfileProvider>
    </I18nProvider>
  </ThemePreferenceProvider>
);

beforeEach(() => {
  jest.useFakeTimers({ advanceTimers: true, now: new Date("2026-11-10T04:00:00Z") });
  mockApi.sent.length = 0;
  mockSession = DONOR;
  jest.mocked(router.push).mockClear();
  mockApi.serve({
    "GET /api/me": () => json(200, { fullName: "Nimali Perera", displayName: null, preferredLanguage: "en" }),
    "GET /api/bookings/mine": () => json(200, MINE),
    "PUT /api/me/language": () => new Response(null, { status: 204 }),
    "PUT /api/me/devices": () => new Response(null, { status: 204 }),
    "POST /api/me/devices/remove": () => new Response(null, { status: 204 }),
  });
  mockPhone.stored.clear();
  mockPhone.device.supported = true;
  mockPhone.phone.permission = permissions.GRANTED;
  mockPhone.phone.answer = permissions.GRANTED;
  mockClient.logout.mockClear();
  mockClient.logoutAll.mockClear();
  mockClient.switchInstitution.mockReset().mockResolvedValue({ ok: true });
  mockClient.switchToPersonal.mockReset().mockResolvedValue({ ok: true });
});

afterEach(() => {
  jest.useRealTimers();
});

describe("signed in", () => {
  it("greets the donor by first name and adds up their giving from served meals only", async () => {
    const screen = await render(page());

    screen.getText("Hello, Nimali");
    screen.getText("Nimali Perera");
    screen.getText("NP");
    screen.getLabel("2 Coming up");
    screen.getLabel("2 Meals given");
    screen.getLabel("40 People fed");
  });

  it("shows the soonest booking still to come", async () => {
    const screen = await render(page());

    screen.getText("Your next meal");
    screen.getText("Dinner · 11:30");
  });

  it("changes the account's language too, so texts follow it", async () => {
    const screen = await render(page());

    await press(screen.getText("සිංහල"));

    expect(mockApi.sent).toContainEqual({ method: "PUT", path: "/api/me/language", body: { language: "si" }, signedIn: true });
    screen.getText("භාෂාව");
  });
});

describe("acting as", () => {
  const TWO: SessionState = {
    ...STAFF,
    institutions: [
      { slug: "alpha-temple", name: "Alpha Temple" },
      { slug: "beta-home", name: "Beta Home" },
    ],
  } as SessionState;

  const chosen = (screen: Screen) =>
    screen
      .where((props) => props.accessibilityRole === "radio" && (props.accessibilityState as { selected?: boolean })?.selected === true)
      .map((node) => node.queryAll((child) => child.type === "Text")[0].children.join(""))
      .filter((label) => label === "My own account" || label === "Alpha Temple" || label === "Beta Home");

  it("is not there for a donor who belongs nowhere, or a visitor", async () => {
    const donor = await render(page());
    expect(donor.queryText("Acting as")).toBeNull();

    mockSession = ANONYMOUS;
    const visitor = await render(page());
    expect(visitor.queryText("Acting as")).toBeNull();
  });

  it("lists their own account and each institution, the current one chosen", async () => {
    mockSession = TWO;

    const screen = await render(page());

    screen.getText("Acting as");
    screen.getText("My own account");
    screen.getText("Beta Home");
    expect([...new Set(chosen(screen))]).toEqual(["Alpha Temple"]);
  });

  it("chooses their own account when the session has no institution", async () => {
    mockSession = { ...TWO, activeInstitution: null } as SessionState;

    const screen = await render(page());

    expect([...new Set(chosen(screen))]).toEqual(["My own account"]);
  });

  it("moves to another institution by its slug", async () => {
    mockSession = TWO;
    const screen = await render(page());

    await press(screen.getText("Beta Home"));

    expect(mockClient.switchInstitution).toHaveBeenCalledWith("beta-home");
    expect(mockClient.switchToPersonal).not.toHaveBeenCalled();
  });

  it("goes back to their own account", async () => {
    mockSession = TWO;
    const screen = await render(page());

    await press(screen.getText("My own account"));

    expect(mockClient.switchToPersonal).toHaveBeenCalledTimes(1);
    expect(mockClient.switchInstitution).not.toHaveBeenCalled();
  });

  it("does nothing for the one already chosen", async () => {
    mockSession = TWO;
    const screen = await render(page());

    await press(screen.allMatching("Alpha Temple").at(-1)!);

    expect(mockClient.switchInstitution).not.toHaveBeenCalled();
  });

  it("takes one choice at a time", async () => {
    mockSession = TWO;
    let finish: (result: SwitchInstitutionResult) => void = () => {};
    mockClient.switchInstitution.mockReturnValue(new Promise((resolve) => (finish = resolve)));
    const screen = await render(page());

    await press(screen.getText("Beta Home"));
    await press(screen.getText("My own account"));

    expect(mockClient.switchToPersonal).not.toHaveBeenCalled();
    await act(async () => finish({ ok: true }));
    await settle();
    await press(screen.getText("My own account"));
    expect(mockClient.switchToPersonal).toHaveBeenCalledTimes(1);
  });

  it.each<[string, SwitchInstitutionResult, string]>([
    ["an institution no longer theirs", { ok: false, reason: "not_available" }, "You can no longer act for this institution."],
    ["a lost connection", { ok: false, reason: "unavailable" }, "Can't reach Sethika right now. Check your connection and try again."],
  ])("says so after %s, and lets them choose again", async (_, result, words) => {
    mockSession = TWO;
    mockClient.switchInstitution.mockResolvedValue(result);
    const screen = await render(page());

    await press(screen.getText("Beta Home"));
    screen.getText(words);

    mockClient.switchInstitution.mockResolvedValue({ ok: true });
    await press(screen.getText("Beta Home"));
    expect(screen.queryText(words)).toBeNull();
  });

  it("says nothing when the session was over: the screen shows the ways in", async () => {
    mockSession = TWO;
    mockClient.switchInstitution.mockResolvedValue({ ok: false, reason: "signed_out" });
    const screen = await render(page());

    await press(screen.getText("Beta Home"));

    expect(screen.queryText("You can no longer act for this institution.")).toBeNull();
    expect(screen.where((props) => props.accessibilityRole === "alert")).toEqual([]);
  });
});

describe("changing the password", () => {
  it("opens from the account's group, signed in", async () => {
    const screen = await render(page());

    await press(screen.getText("Change your password"));

    expect(router.push).toHaveBeenCalledWith("/change-password");
  });

  it("is not offered to a visitor", async () => {
    mockSession = ANONYMOUS;

    const screen = await render(page());

    expect(screen.queryText("Change your password")).toBeNull();
  });
});

describe("signed out", () => {
  beforeEach(() => {
    mockSession = ANONYMOUS;
  });

  it("says what an account is for, offers the ways in, and asks the API for nothing", async () => {
    const screen = await render(page());

    screen.getText("Give a meal, from wherever you are");
    expect(screen.queryText("Meals given")).toBeNull();
    expect(screen.queryText("Sign out")).toBeNull();

    await press(screen.getText("Create an account"));
    expect(router.push).toHaveBeenCalledWith("/sign-up");
    expect(mockApi.sent).toEqual([]);
  });

  it("changes only this phone's language, without asking the API", async () => {
    const screen = await render(page());
    await press(screen.getText("தமிழ்"));
    expect(mockApi.sent).toEqual([]);
  });
});

describe("the push switch", () => {
  const LABEL = "Notifications on this phone";
  const HELP = "Your booking updates and reminders. Texts still come either way.";
  const BLOCKED = "Notifications are off for Sethika in your phone's settings.";
  const FAILED = "That did not work. Check your connection and try again.";
  const removals = () => mockApi.sent.filter((request) => request.path === "/api/me/devices/remove");

  const isOn = (screen: Screen) => screen.getLabel(LABEL).props.value as boolean;

  async function flip(screen: Screen, on: boolean) {
    await act(async () => {
      screen.getLabel(LABEL).props.onChange({ nativeEvent: { value: on } });
    });
    await settle();
  }

  function registered() {
    mockPhone.stored.set(CHOICE_KEY, "on");
    mockPhone.stored.set(TOKEN_KEY, TOKEN);
  }

  it("shows on for a phone that is registered, and says texts still come", async () => {
    registered();

    const screen = await render(page());

    expect(isOn(screen)).toBe(true);
    screen.getText(HELP);
    expect(screen.queryText(BLOCKED)).toBeNull();
  });

  it("goes off by removing this phone, with the token in the body", async () => {
    registered();
    const screen = await render(page());

    await flip(screen, false);

    expect(isOn(screen)).toBe(false);
    expect(removals()).toEqual([{ method: "POST", path: "/api/me/devices/remove", body: { expoPushToken: TOKEN }, signedIn: true }]);
  });

  it("goes on by registering again", async () => {
    mockPhone.stored.set(CHOICE_KEY, "off");
    const screen = await render(page());
    expect(isOn(screen)).toBe(false);
    expect(mockApi.sent.filter((request) => request.path === "/api/me/devices")).toEqual([]);

    await flip(screen, true);

    expect(isOn(screen)).toBe(true);
    expect(mockApi.sent.filter((request) => request.path === "/api/me/devices")).toHaveLength(1);
  });

  it("stays on and says so when it could not go off, and the next try clears the words", async () => {
    registered();
    mockApi.also({ "POST /api/me/devices/remove": () => new Response(null, { status: 500 }) });
    const screen = await render(page());

    await flip(screen, false);
    expect(isOn(screen)).toBe(true);
    screen.getText(FAILED);

    mockApi.also({ "POST /api/me/devices/remove": () => new Response(null, { status: 204 }) });
    await flip(screen, false);
    expect(isOn(screen)).toBe(false);
    expect(screen.queryText(FAILED)).toBeNull();
  });

  it("cannot be flipped again while a change is on its way", async () => {
    registered();
    let answer = (_: Response) => {};
    mockApi.also({ "POST /api/me/devices/remove": () => new Promise<Response>((resolve) => (answer = resolve)) });
    const screen = await render(page());
    expect(screen.getLabel(LABEL).props.disabled).toBeFalsy();

    await flip(screen, false);
    expect(screen.getLabel(LABEL).props.disabled).toBe(true);

    answer(new Response(null, { status: 204 }));
    await settle();
    expect(screen.getLabel(LABEL).props.disabled).toBeFalsy();
  });

  it("points to the phone's settings when the phone will not ask again", async () => {
    const openSettings = jest.spyOn(Linking, "openSettings").mockResolvedValue();
    mockPhone.stored.set(CHOICE_KEY, "off");
    mockPhone.phone.permission = permissions.UNDECIDED;
    mockPhone.phone.answer = permissions.BLOCKED;
    const screen = await render(page());
    expect(screen.queryText("Open settings")).toBeNull();

    await flip(screen, true);

    expect(isOn(screen)).toBe(false);
    screen.getText(BLOCKED);
    expect(screen.queryText(HELP)).toBeNull();
    await press(screen.getText("Open settings"));
    expect(openSettings).toHaveBeenCalledTimes(1);
    openSettings.mockRestore();
  });

  it("reads the phone again on coming back from its settings, and only then", async () => {
    let changed: (state: string) => void = () => {};
    const remove = jest.fn();
    const listen = jest.spyOn(AppState, "addEventListener").mockImplementation((_, handler) => {
      changed = handler as typeof changed;
      return { remove };
    });
    mockPhone.stored.set(CHOICE_KEY, "on");
    mockPhone.phone.permission = permissions.BLOCKED;
    const screen = await render(page());
    expect(isOn(screen)).toBe(false);

    mockPhone.phone.permission = permissions.GRANTED;
    await act(async () => changed("background"));
    await settle();
    expect(isOn(screen)).toBe(false);

    await act(async () => changed("active"));
    await settle();
    expect(isOn(screen)).toBe(true);
    expect(screen.queryText(BLOCKED)).toBeNull();

    screen.unmount();
    expect(remove).toHaveBeenCalled();
    listen.mockRestore();
  });

  it("is not there in Expo Go", async () => {
    mockPhone.device.supported = false;

    const screen = await render(page());

    expect(screen.queryLabel(LABEL)).toBeNull();
  });

  it("is not there for a visitor", async () => {
    mockSession = ANONYMOUS;

    const screen = await render(page());

    expect(screen.queryLabel(LABEL)).toBeNull();
  });
});

describe("signing out", () => {
  const steps = () => mockApi.sent.map((request) => request.path).filter((path) => path.startsWith("logout") || path.endsWith("/remove"));

  beforeEach(() => {
    mockPhone.stored.set(CHOICE_KEY, "on");
    mockPhone.stored.set(TOKEN_KEY, TOKEN);
  });

  it.each([
    ["Sign out", "logout"],
    ["Sign out on all devices", "logout-all"],
  ])("%s removes this phone first, while the session still works", async (label, end) => {
    const screen = await render(page());

    await press(screen.getText(label));

    expect(steps()).toEqual(["/api/me/devices/remove", end]);
    expect(mockPhone.stored.has(TOKEN_KEY)).toBe(false);
  });

  it("still signs out when the removal fails", async () => {
    mockApi.also({ "POST /api/me/devices/remove": () => Promise.reject(new Error("Network request failed")) });
    const screen = await render(page());

    await press(screen.getText("Sign out"));

    expect(mockClient.logout).toHaveBeenCalledTimes(1);
  });
});

describe("appearance", () => {
  it("hands the choice to the whole app, and Auto back to the phone's setting", async () => {
    const setColorScheme = jest.spyOn(Appearance, "setColorScheme").mockImplementation(() => {});
    const screen = await render(page());

    await press(screen.getText("Dark"));
    expect(setColorScheme).toHaveBeenLastCalledWith("dark");

    await press(screen.getText("Auto"));
    expect(setColorScheme).toHaveBeenLastCalledWith("unspecified");
    setColorScheme.mockRestore();
  });
});
