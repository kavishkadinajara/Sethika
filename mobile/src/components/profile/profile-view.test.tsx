import { router } from "expo-router";
import { Appearance } from "react-native";

import type { SessionState } from "@/auth/session-client";
import { ProfileProvider } from "@/auth/profile-provider";
import { I18nProvider } from "@/i18n/i18n-provider";
import { ANONYMOUS, createApiStub, DONOR } from "@/test-utils/api-stub";
import { booking } from "@/test-utils/fixtures";
import { json, press, render } from "@/test-utils/render";
import { ThemePreferenceProvider } from "@/theme/appearance";

import { ProfileView } from "./profile-view";

/*  The Profile tab against GET /api/me and GET /api/bookings/mine, stubbed at the app's client
    boundary, inside the real profile, language and theme providers. Today is 2026-11-10.     */

const mockApi = createApiStub();
let mockSession: SessionState = DONOR;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
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
        <ProfileView />
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
  });
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
