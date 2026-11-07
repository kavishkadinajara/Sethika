import { router } from "expo-router";
import { StyleSheet } from "react-native";

import type { SessionState } from "@/auth/session-client";
import { ANONYMOUS, createApiStub, DONOR } from "@/test-utils/api-stub";
import { booking } from "@/test-utils/fixtures";
import { json, press, render, type } from "@/test-utils/render";
import { colors } from "@/theme/colors";

import { MyBookings } from "./my-bookings";

/*  My bookings against GET /api/bookings/mine and POST /api/bookings/{no}/cancel, stubbed at the
    app's client boundary. Today is 2026-11-10 in Colombo.                                     */

const mockApi = createApiStub();
let mockSession: SessionState = DONOR;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("expo-router", () => ({
  router: { push: jest.fn(), navigate: jest.fn(), dismissTo: jest.fn() },
  useFocusEffect: (effect: () => void) => jest.requireActual<typeof import("react")>("react").useEffect(effect, [effect]),
}));
jest.mock("react-native-safe-area-context", () => jest.requireActual("react-native-safe-area-context/jest/mock").default);

const MINE = [
  booking({ bookingNo: "BK-2026-000001", date: "2026-11-30", status: "Approved", canCancel: true }),
  booking({ bookingNo: "BK-2026-000002", date: "2026-11-01", status: "Completed", canCancel: false }),
  booking({ bookingNo: "BK-2026-000003", date: "2026-11-12", status: "Requested", canCancel: true }),
  booking({ bookingNo: "BK-2026-000004", date: "2026-11-15", status: "Rejected", canCancel: false, rejectReason: "Closed for the festival" }),
  booking({ bookingNo: "BK-2026-000005", date: "2026-11-11", status: "Approved", canCancel: false }),
];

const order = (allText: string[]) => allText.filter((line) => /BK-2026-\d+$/.test(line)).map((line) => line.slice(-14));

beforeEach(() => {
  jest.useFakeTimers({ advanceTimers: true, now: new Date("2026-11-10T04:00:00Z") });
  mockApi.sent.length = 0;
  mockSession = DONOR;
  mockApi.serve({ "GET /api/bookings/mine": () => json(200, MINE) });
});

afterEach(() => {
  jest.useRealTimers();
});

it("lists what is coming soonest first, then the rest latest first", async () => {
  const screen = await render(<MyBookings />);

  const text = screen.allText();
  expect(text.indexOf("Coming up")).toBeLessThan(text.indexOf("Earlier"));
  expect(order(text)).toEqual(["BK-2026-000005", "BK-2026-000003", "BK-2026-000001", "BK-2026-000004", "BK-2026-000002"]);
});

it("says each booking's status in words, and why one was not accepted", async () => {
  const screen = await render(<MyBookings />);

  screen.getText("Waiting for reply");
  screen.getText("Not accepted");
  screen.getText("Served");
  screen.getText("Not accepted: Closed for the festival");
  screen.getText(/^Tomorrow · 25 people/);
});

it("offers cancel only on the bookings the API says the donor may cancel", async () => {
  const screen = await render(<MyBookings />);

  const cancellable = MINE.filter((b) => b.canCancel).map((b) => b.bookingNo);
  for (const { bookingNo } of MINE) {
    const card = screen.getTestId(`booking-${bookingNo}`);
    const hasCancel = card.queryAll((node) => node.type === "Text" && node.children.join("") === "Cancel booking").length > 0;
    expect([bookingNo, hasCancel]).toEqual([bookingNo, cancellable.includes(bookingNo)]);
  }
});

it("cancels with the reason given, then shows the list again", async () => {
  const screen = await render(<MyBookings />);
  mockApi.also({ "POST /api/bookings/BK-2026-000003/cancel": () => json(200, { bookingNo: "BK-2026-000003", status: "Cancelled" }) });

  const card = screen.getTestId("booking-BK-2026-000003");
  await press(card.queryAll((node) => node.type === "Text" && node.children.join("") === "Cancel booking")[0]);
  screen.getText("Cancel this booking?");
  await type(screen.getLabel("Reason (optional)"), " Travelling that week ");
  // The sheet's confirm button, after the cards' own.
  await press(screen.allMatching("Cancel booking").at(-1)!);

  const cancel = mockApi.sent.find((request) => request.method === "POST");
  expect(cancel).toEqual({ method: "POST", path: "/api/bookings/BK-2026-000003/cancel", body: { reason: "Travelling that week" }, signedIn: true });
  screen.getText("BK-2026-000003 is cancelled.");
  expect(mockApi.sent.filter((request) => request.path === "/api/bookings/mine")).toHaveLength(2);
});

it("lets a regular day go, never cancels it, and shows the list again", async () => {
  const regular = booking({ bookingNo: "BK-2026-000006", date: "2026-11-10", status: "Approved", canCancel: false, regular: true });
  mockApi.serve({
    "GET /api/bookings/mine": () => json(200, [regular]),
    "POST /api/bookings/BK-2026-000006/skip": () => new Response(null, { status: 204 }),
  });
  const screen = await render(<MyBookings />);

  screen.getText("A regular day the institution set up for you");
  expect(screen.queryText("Cancel booking")).toBeNull();
  await press(screen.getText("I can't give that day"));
  screen.getText("Can't give on this day?");
  await type(screen.getLabel("Reason (optional)"), " Away in Kandy ");
  await press(screen.allMatching("I can't give that day").at(-1)!);

  expect(mockApi.sent.find((request) => request.method === "POST")).toEqual({
    method: "POST",
    path: "/api/bookings/BK-2026-000006/skip",
    body: { reason: "Away in Kandy" },
    signedIn: true,
  });
  screen.getText("BK-2026-000006 is let go. We will text you to confirm.");
  expect(mockApi.sent.filter((request) => request.path === "/api/bookings/mine")).toHaveLength(2);
});

it("offers no letting go on an ordinary booking", async () => {
  const screen = await render(<MyBookings />);

  expect(screen.queryText("I can't give that day")).toBeNull();
  expect(screen.queryText("A regular day the institution set up for you")).toBeNull();
});

it("says why a cancel was refused", async () => {
  const screen = await render(<MyBookings />);
  mockApi.also({ "POST /api/bookings/BK-2026-000001/cancel": () => json(409, { code: "cancel_window_closed" }) });

  await press(screen.getTestId("booking-BK-2026-000001").queryAll((node) => node.type === "Text" && node.children.join("") === "Cancel booking")[0]);
  await press(screen.allMatching("Cancel booking").at(-1)!);

  screen.getText("It is too late to cancel this booking yourself. Please call the institution.");
});

it("asks a visitor to sign in, and asks the API for nothing", async () => {
  mockSession = ANONYMOUS;
  const screen = await render(<MyBookings />);

  screen.getText("Sign in to see your bookings. New to Sethika? Create an account.");
  await press(screen.getText("Sign in"));
  expect(router.push).toHaveBeenCalledWith("/sign-in");
  expect(mockApi.sent).toEqual([]);
});

it("offers a way to find a place when there are no bookings yet", async () => {
  mockApi.serve({ "GET /api/bookings/mine": () => json(200, []) });
  const screen = await render(<MyBookings />);

  screen.getText("You have not sponsored a meal yet.");
  await press(screen.getText("Find a place to give"));
  expect(router.navigate).toHaveBeenCalledWith("/");
});

describe("as ledger lines", () => {
  it("gives each booking its day and time in the narrow column, then the meal and the place", async () => {
    mockApi.serve({ "GET /api/bookings/mine": () => json(200, [booking({ date: "2026-11-20", status: "Approved" })]) });
    const screen = await render(<MyBookings />);

    const text = screen.allText();
    const at = (line: string) => text.indexOf(line);
    expect(at("Fri, 20 Nov")).toBeGreaterThan(-1);
    expect(at("Fri, 20 Nov")).toBeLessThan(at("11:30"));
    expect(at("11:30")).toBeLessThan(at("Lunch"));
    expect(at("Lunch")).toBeLessThan(at("Test Elders' Home"));
  });

  it("draws the gold rim once, on what is coming, and a rule between lines only", async () => {
    const screen = await render(<MyBookings />);

    const flat = (style: unknown) => (StyleSheet.flatten(style as never) ?? {}) as { height?: number; borderTopWidth?: number };
    expect(screen.where((props) => flat(props.style).height === 3)).toHaveLength(1);

    const ruled = MINE.map((item) => flat(screen.getTestId(`booking-${item.bookingNo}`).props.style).borderTopWidth ?? 0);
    // Coming up: 000005, 000003, 000001. Earlier: 000004, 000002. Each section's first has no rule.
    expect(ruled).toEqual([1, 1, 1, 0, 0]);
  });

  it("prints a line that is over quietly, and one still to come plainly", async () => {
    const screen = await render(<MyBookings />);
    const colour = (line: string) => (StyleSheet.flatten(screen.getText(line).props.style) as { color: string }).color;

    expect(colour("Sun, 1 Nov")).toBe(colors.light.muted);
    expect(colour("Wed, 11 Nov")).toBe(colors.light.fg);
  });

  it("keeps the reason, the wait and the way out on the line", async () => {
    const screen = await render(<MyBookings />);
    within(screen, "booking-BK-2026-000004", "Not accepted: Closed for the festival");
    within(screen, "booking-BK-2026-000003", "Waiting for their reply. We will text you.");
    within(screen, "booking-BK-2026-000003", "Cancel booking");
  });
});

function within(screen: Awaited<ReturnType<typeof render>>, id: string, words: string) {
  const texts = screen.getTestId(id).queryAll((node) => node.type === "Text");
  const line = texts.some((node) => node.children.join("") === words);
  expect(line).toBe(true);
}
