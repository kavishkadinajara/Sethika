import { act } from "react";
import { StyleSheet } from "react-native";

import ExploreScreen from "@/app/(app)/index";
import type { SessionState } from "@/auth/session-client";
import { addDays, colomboToday } from "@/bookings/calendar";
import { ANONYMOUS, createApiStub, DONOR } from "@/test-utils/api-stub";
import { booking, institution, searchAnswer, searchPlace } from "@/test-utils/fixtures";
import { json, press, render, settle, type Screen } from "@/test-utils/render";
import { brand } from "@/theme/colors";

/*  Explore's wiring: the Day frame over Near me over the directory, and one pull for all three. */

const mockApi = createApiStub();
let mockSession: SessionState = ANONYMOUS;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("@/auth/permission-provider", () => ({ usePermissions: () => ({ ready: true, can: (code: string) => code === "MYBOOKINGS.BOOKINGS.VIEW" }) }));
jest.mock("@/auth/profile-provider", () => ({ useProfile: () => null, greetingName: () => null }));
jest.mock("expo-router", () => ({
  router: { push: jest.fn(), navigate: jest.fn() },
  useFocusEffect: (effect: () => void) => jest.requireActual<typeof import("react")>("react").useEffect(effect, [effect]),
}));
jest.mock("react-native-webview", () => ({ WebView: () => null }));
jest.mock("react-native/Libraries/Utilities/useColorScheme", () => ({ __esModule: true, default: () => "dark" }));
jest.mock("@/bookings/native-location", () => ({
  nativeLocation: {
    requestPermission: () => Promise.resolve(true),
    lastKnown: () => Promise.resolve({ latitude: 6.927, longitude: 79.862 }),
    current: () => Promise.reject(new Error("not needed")),
  },
}));

const tomorrow = addDays(colomboToday(), 1);
const SEARCH = `GET /api/search?date=${tomorrow}&near=6.927,79.862&radiusKm=10`;
const count = (path: string) => mockApi.sent.filter((request) => request.path.startsWith(path)).length;

// React Native's Jest mock draws the control without its props, so read what the list was given.
const refresher = (screen: Screen) => {
  const [list] = screen.where((props) => "refreshControl" in props);
  return list.props.refreshControl as { props: { refreshing: boolean; onRefresh: () => void } };
};

async function pull(screen: Screen) {
  await act(async () => {
    refresher(screen).props.onRefresh();
  });
  await settle();
}

beforeEach(() => {
  mockApi.sent.length = 0;
  mockSession = ANONYMOUS;
  mockApi.serve({
    "GET /api/bookings/mine": () => json(200, [booking({ date: addDays(colomboToday(), 5), slotName: "My lunch" })]),
    "GET /api/institutions": () => json(200, [institution({ name: "Listed Home" })]),
    [SEARCH]: () => json(200, searchAnswer(tomorrow, [searchPlace({ name: "Open Nearby" })])),
  });
});

it("shows the directory under Near me, and a pull reads the directory again", async () => {
  const screen = await render(<ExploreScreen />);
  screen.getText("Near me");
  screen.getText("Listed Home");

  await pull(screen);

  expect(count("/api/institutions")).toBe(2);
  expect(count("/api/search")).toBe(0);
  expect(refresher(screen).props.refreshing).toBe(false);
});

it("replaces the directory with the list, and a pull there searches again and stops the spinner", async () => {
  const screen = await render(<ExploreScreen />);
  await press(screen.getText("Near me"));
  screen.getText("Open Nearby");
  expect(screen.queryText("Listed Home")).toBeNull();

  await pull(screen);

  expect(count("/api/search")).toBe(2);
  expect(refresher(screen).props.refreshing).toBe(false);
});

it("still stops the spinner when the donor leaves the list during a pull", async () => {
  const screen = await render(<ExploreScreen />);
  await press(screen.getText("Near me"));
  mockApi.also({ [SEARCH]: () => new Promise<Response>(() => {}) });

  await pull(screen);
  expect(refresher(screen).props.refreshing).toBe(true);

  await press(screen.getText("Show every place"));

  screen.getText("Listed Home");
  expect(refresher(screen).props.refreshing).toBe(false);
});

describe("under the Day frame", () => {
  const position = (screen: Screen, words: string | RegExp) => screen.allText().findIndex((line) => (typeof words === "string" ? line === words : words.test(line)));

  it("reads from the top: the day, the table, then the places", async () => {
    mockSession = DONOR;
    const screen = await render(<ExploreScreen />);

    const order = [/^Your next meal/, "Your table", "Find a place to give", "Near me", "Listed Home"].map((words) => position(screen, words));
    expect(order).toEqual([...order].sort((a, b) => a - b));
    expect(order).not.toContain(-1);
  });

  it("has no green field in the dark (F21)", async () => {
    mockSession = DONOR;
    const screen = await render(<ExploreScreen />);
    const green = screen.where((props) => (StyleSheet.flatten(props.style as never) as { backgroundColor?: string } | undefined)?.backgroundColor === brand.estate);
    expect(green).toHaveLength(0);
  });

  it("keeps the frame over the near-me list, and the list keeps working", async () => {
    mockSession = DONOR;
    const screen = await render(<ExploreScreen />);

    await press(screen.getText("Near me"));

    screen.getText("Open Nearby");
    screen.getText("Your table");
    expect(position(screen, "Your table")).toBeLessThan(position(screen, "Open Nearby"));
    expect(screen.queryText("Listed Home")).toBeNull();
  });

  it("reads the donor's day again on a pull, in either list, and keeps the near-me list", async () => {
    mockSession = DONOR;
    const screen = await render(<ExploreScreen />);
    expect(count("/api/bookings/mine")).toBe(1);

    await pull(screen);
    expect(count("/api/bookings/mine")).toBe(2);

    await press(screen.getText("Near me"));
    await pull(screen);

    expect(count("/api/bookings/mine")).toBe(3);
    screen.getText("Open Nearby");
    expect(refresher(screen).props.refreshing).toBe(false);
  });

  it("stops the spinner when the donor's read never answers", async () => {
    mockSession = DONOR;
    const screen = await render(<ExploreScreen />);
    mockApi.also({ "GET /api/bookings/mine": () => new Promise<Response>(() => {}) });

    await pull(screen);

    expect(refresher(screen).props.refreshing).toBe(false);
  });

  it("still shows the places when the donor's read fails", async () => {
    mockSession = DONOR;
    mockApi.also({ "GET /api/bookings/mine": () => json(500, {}) });

    const screen = await render(<ExploreScreen />);

    screen.getText("Listed Home");
    screen.getText("Near me");
  });
});
