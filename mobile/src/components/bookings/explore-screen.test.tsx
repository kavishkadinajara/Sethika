import { act } from "react";

import ExploreScreen from "@/app/(app)/index";
import { addDays, colomboToday } from "@/bookings/calendar";
import { createApiStub } from "@/test-utils/api-stub";
import { institution, searchAnswer, searchPlace } from "@/test-utils/fixtures";
import { json, press, render, settle, type Screen } from "@/test-utils/render";

/*  Explore's wiring: the directory under Near me, and one pull-to-refresh spinner for both. */

const mockApi = createApiStub();

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => ({ status: "anonymous" }), useSessionClient: () => mockApi.client }));
jest.mock("@/auth/profile-provider", () => ({ useProfile: () => null, greetingName: () => null }));
jest.mock("expo-router", () => ({ router: { push: jest.fn() } }));
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
  mockApi.serve({
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
