import { act } from "react";
import { Linking } from "react-native";

import type { SessionState } from "@/auth/session-client";
import { ANONYMOUS, createApiStub, DONOR, STAFF } from "@/test-utils/api-stub";
import { calendarBooking, slotDay, tableSlot } from "@/test-utils/fixtures";
import { isDisabled, json, press, render, settle, type Screen } from "@/test-utils/render";

import { TodayView } from "./today-view";

/*  The Today tab against GET /api/institution/today, /bookings and POST .../complete, stubbed at
    the app's client boundary. It is 09:30 on 2026-11-10 in Colombo. The server's date is a day
    later than the phone's in one test, to show which of the two decides.                      */

const mockApi = createApiStub();
let mockSession: SessionState = STAFF;
let mockHeld: string[] = [];
let mockReady = true;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("@/auth/permission-provider", () => ({
  usePermissions: () => ({ ready: mockReady, can: (code: string) => mockHeld.includes(code) }),
}));
jest.mock("expo-router", () => ({
  router: { push: jest.fn(), navigate: jest.fn() },
  useFocusEffect: (effect: () => void) => jest.requireActual<typeof import("react")>("react").useEffect(effect, [effect]),
}));
jest.mock("react-native-safe-area-context", () => jest.requireActual("react-native-safe-area-context/jest/mock").default);

const VIEW = "BOOKINGS.CALENDAR.VIEW";
const EDIT = "BOOKINGS.CALENDAR.EDIT";
const APPROVE = "BOOKINGS.CALENDAR.APPROVE";
const REJECT = "BOOKINGS.CALENDAR.REJECT";

const DATE = "2026-11-10";
const LUNCH = "lunch-slot";
const DINNER = "dinner-slot";
const NO = "BK-2026-000001";

const TABLE = {
  date: DATE,
  slots: [
    tableSlot({ mealSlot: LUNCH, name: "Lunch", startTime: "11:30", maxSponsors: 4, bookedSponsors: 1, state: "Open" }),
    tableSlot({ mealSlot: DINNER, name: "Dinner", startTime: "18:00", maxSponsors: 2, bookedSponsors: 0, state: "Open" }),
  ],
};
const DAYS = [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ bookingNo: NO, donorName: "Nimali Perera", donorPhone: "+94771234567" })] })];

const TODAY_PATH = "GET /api/institution/today";
const DAY_PATH = `GET /api/institution/bookings?from=${DATE}&to=${DATE}`;
const WAITING_PATH = `GET /api/institution/bookings?from=${DATE}&to=2027-01-10&status=Requested`;
const SERVE_PATH = `POST /api/institution/bookings/${NO}/complete`;

const served = () => mockApi.sent.filter((request) => request.path.endsWith("/complete"));

function serveOk() {
  return json(200, { bookingNo: NO, status: "Completed" });
}

/** Opens the confirm step of the one booking. */
async function ask(screen: Screen) {
  await press(screen.getText("Mark as served"));
}

beforeEach(() => {
  jest.useFakeTimers({ advanceTimers: true, now: new Date("2026-11-10T04:00:00Z") });
  mockApi.sent.length = 0;
  mockSession = STAFF;
  mockHeld = [VIEW, EDIT];
  mockReady = true;
  mockApi.serve({
    [TODAY_PATH]: () => json(200, TABLE),
    [DAY_PATH]: () => json(200, DAYS),
    [SERVE_PATH]: serveOk,
  });
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

describe("who sees it", () => {
  it("waits for the permissions, and asks the API nothing meanwhile", async () => {
    mockReady = false;
    mockHeld = [];

    const screen = await render(<TodayView />);

    screen.getLabel("Loading");
    expect(mockApi.sent).toEqual([]);
  });

  it.each<[string, SessionState, string[]]>([
    ["a role without the calendar", STAFF, [EDIT]],
    ["someone on their own account", DONOR, [VIEW]],
    ["a visitor", ANONYMOUS, [VIEW]],
  ])("says no to %s, and sends no query", async (_, session, held) => {
    mockSession = session;
    mockHeld = held;

    const screen = await render(<TodayView />);

    screen.getText("Your role in this institution does not include this page.");
    expect(mockApi.sent).toEqual([]);
  });
});

describe("the day", () => {
  it("heads the page with the institution, the date and the sentence", async () => {
    const screen = await render(<TodayView />);

    screen.getText("Alpha Temple");
    screen.getText(/10 November 2026/);
    screen.getText("Next on the table is Lunch at 11:30, and 5 places are still open today.");
  });

  it("shows each meal, its places and its sponsors, and a meal with none says so", async () => {
    const screen = await render(<TodayView />);

    screen.getText("11:30 · Lunch");
    screen.getText("1 of 4 taken");
    screen.getText("18:00 · Dinner");
    screen.getText("0 of 2 taken");
    screen.getText("Nimali Perera");
    screen.getText("No sponsor yet.");
  });

  it("gives each sponsor's details in words", async () => {
    mockApi.also({
      [DAY_PATH]: () =>
        json(200, [
          slotDay({
            date: DATE,
            mealSlot: LUNCH,
            bookings: [
              calendarBooking({
                bookingNo: NO,
                headcount: 25,
                fulfilmentMode: "DonorBrings",
                inMemoryOf: "Sunil Perera",
                notes: "No nuts, please",
                standing: "0193f0c2-0000-7000-8000-000000000009",
              }),
            ],
          }),
        ]),
    });

    const screen = await render(<TodayView />);

    screen.getText(`25 people · The sponsor brings the meal · ${NO}`);
    screen.getText("In memory of Sunil Perera");
    screen.getText("No nuts, please");
    screen.getText("Regular donor");
    screen.getText("Confirmed");
  });

  it("leaves the extras out of an ordinary booking", async () => {
    const screen = await render(<TodayView />);

    expect(screen.queryText("Regular donor")).toBeNull();
    expect(screen.queryText(/^In memory of/)).toBeNull();
  });

  it("calls a sponsor from the number", async () => {
    const call = jest.spyOn(Linking, "openURL").mockResolvedValue(undefined);
    const screen = await render(<TodayView />);

    screen.getText("+94771234567");
    await press(screen.getLabel("Call Nimali Perera"));

    expect(call).toHaveBeenCalledWith("tel:+94771234567");
  });

  it("says a full meal, a closed meal and the requests to answer", async () => {
    mockApi.also({
      [TODAY_PATH]: () =>
        json(200, {
          date: DATE,
          slots: [
            tableSlot({ mealSlot: "a", name: "Full one", startTime: "11:00", state: "Full", maxSponsors: 2, bookedSponsors: 2 }),
            tableSlot({ mealSlot: "b", name: "Closed one", startTime: "12:00", state: "Closed" }),
            tableSlot({ mealSlot: "c", name: "Asked one", startTime: "13:00", state: "Open", maxSponsors: 3, bookedSponsors: 1, requested: 2 }),
          ],
        }),
      [DAY_PATH]: () => json(200, []),
    });

    const screen = await render(<TodayView />);

    screen.getText("Every place taken");
    screen.getText("Closed today");
    screen.getText("1 of 3 taken · 2 to answer");
  });

  it("says when no meal is on the table", async () => {
    mockApi.also({ [TODAY_PATH]: () => json(200, { date: DATE, slots: [] }), [DAY_PATH]: () => json(200, []) });

    const screen = await render(<TodayView />);

    screen.getText("No meals today.");
    screen.getText("No meals are on the table today.");
  });

  it("takes the day from the server, not the phone's clock", async () => {
    const tomorrow = "2026-11-11";
    mockApi.also({
      [TODAY_PATH]: () => json(200, { date: tomorrow, slots: [] }),
      [`GET /api/institution/bookings?from=${tomorrow}&to=${tomorrow}`]: () => json(200, []),
    });

    const screen = await render(<TodayView />);

    screen.getText(/11 November 2026/);
    expect(mockApi.sent.at(-1)?.path).toBe(`/api/institution/bookings?from=${tomorrow}&to=${tomorrow}`);
  });
});

describe("requests in the sentence", () => {
  const asked = () => json(200, [slotDay({ bookings: [calendarBooking({ status: "Requested" })] })]);

  it.each([APPROVE, REJECT])("counts them for a role with %s, over the API's widest range", async (code) => {
    mockHeld = [VIEW, code];
    mockApi.also({ [WAITING_PATH]: asked });

    const screen = await render(<TodayView />);

    screen.getText("1 booking waits for your answer, and next on the table is Lunch at 11:30.");
  });

  it("is not read at all for a role that may not answer", async () => {
    await render(<TodayView />);

    expect(mockApi.sent.map((request) => request.path)).toEqual(["/api/institution/today", `/api/institution/bookings?from=${DATE}&to=${DATE}`]);
  });

  it("does not cost the day when only the count fails", async () => {
    mockHeld = [VIEW, APPROVE];
    mockApi.also({ [WAITING_PATH]: () => new Response(null, { status: 500 }) });

    const screen = await render(<TodayView />);

    screen.getText("Nimali Perera");
    screen.getText("Next on the table is Lunch at 11:30, and 5 places are still open today.");
  });
});

describe("a read that fails", () => {
  it.each([
    ["the table", TODAY_PATH],
    ["the sponsors", DAY_PATH],
  ])("offers to try again when %s cannot be read, and recovers", async (_, path) => {
    mockApi.also({ [path]: () => Promise.reject(new Error("offline")) });

    const screen = await render(<TodayView />);
    screen.getText("Can't reach Sethika right now. Check your connection and try again.");

    mockApi.also({ [TODAY_PATH]: () => json(200, TABLE), [DAY_PATH]: () => json(200, DAYS) });
    await press(screen.getText("Try again"));

    screen.getText("Nimali Perera");
    expect(screen.queryText("Try again")).toBeNull();
  });

  it("says a 403 is the role's", async () => {
    mockApi.also({ [TODAY_PATH]: () => json(403, { code: "no_institution" }) });

    const screen = await render(<TodayView />);

    screen.getText("Your role in this institution does not include this page.");
  });

  it("keeps the day on screen when a later read fails, and says the figures may be old", async () => {
    const screen = await render(<TodayView />);

    mockApi.also({ [TODAY_PATH]: () => Promise.reject(new Error("offline")) });
    await screen.rerender(<TodayView refreshKey={1} />);

    screen.getText("Nimali Perera");
    screen.getText("Some of today's figures could not be read.");
  });

  it("drops that line once a read works again", async () => {
    const screen = await render(<TodayView />);
    mockApi.also({ [TODAY_PATH]: () => Promise.reject(new Error("offline")) });
    await screen.rerender(<TodayView refreshKey={1} />);

    mockApi.also({ [TODAY_PATH]: () => json(200, TABLE) });
    await screen.rerender(<TodayView refreshKey={2} />);

    expect(screen.queryText("Some of today's figures could not be read.")).toBeNull();
  });
});

describe("reading again", () => {
  it("reads on a pull, and tells the screen when it is done", async () => {
    const settled = jest.fn();
    const screen = await render(<TodayView onSettled={settled} />);
    const before = mockApi.sent.length;
    settled.mockClear();

    await screen.rerender(<TodayView refreshKey={1} onSettled={settled} />);

    expect(mockApi.sent.length).toBeGreaterThan(before);
    expect(settled).toHaveBeenCalledTimes(1);
  });

  it("stops the pull's spinner after a failure too", async () => {
    const settled = jest.fn();
    mockApi.also({ [TODAY_PATH]: () => Promise.reject(new Error("offline")) });

    await render(<TodayView onSettled={settled} />);

    expect(settled).toHaveBeenCalled();
  });

  it("does not read again for a new onSettled alone", async () => {
    const screen = await render(<TodayView onSettled={() => {}} />);
    const before = mockApi.sent.length;

    await screen.rerender(<TodayView onSettled={() => {}} />);

    expect(mockApi.sent.length).toBe(before);
  });

  it("lets a newer read win over a slower older one", async () => {
    let older: (response: Response) => void = () => {};
    mockApi.also({ [TODAY_PATH]: () => new Promise((resolve) => (older = resolve)) });
    const screen = await render(<TodayView />);

    mockApi.also({ [TODAY_PATH]: () => json(200, TABLE) });
    await screen.rerender(<TodayView refreshKey={1} />);
    screen.getText("1 of 4 taken");

    await act(async () => older(json(200, { date: DATE, slots: [tableSlot({ mealSlot: LUNCH, maxSponsors: 9, bookedSponsors: 8 })] })));
    await settle();

    screen.getText("1 of 4 taken");
    expect(screen.queryText("8 of 9 taken")).toBeNull();
  });
});

describe("served", () => {
  it("is offered on a confirmed booking to a role that edits the calendar", async () => {
    const screen = await render(<TodayView />);

    screen.getText("Mark as served");
  });

  it("is not offered to a role that only reads", async () => {
    mockHeld = [VIEW];

    const screen = await render(<TodayView />);

    screen.getText("Nimali Perera");
    expect(screen.queryText("Mark as served")).toBeNull();
  });

  it.each(["Requested", "Completed", "NoShow"] as const)("is not offered on a booking that is %s", async (status) => {
    mockApi.also({ [DAY_PATH]: () => json(200, [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ status })] })]) });

    const screen = await render(<TodayView />);

    expect(screen.queryText("Mark as served")).toBeNull();
  });

  it("does nothing on the first tap but ask", async () => {
    const screen = await render(<TodayView />);

    await ask(screen);

    screen.getText("Has Nimali Perera given this meal? This cannot be undone.");
    expect(served()).toEqual([]);
  });

  it("can be backed out of, sending nothing", async () => {
    const screen = await render(<TodayView />);
    await ask(screen);

    await press(screen.getText("Back"));

    screen.getText("Mark as served");
    expect(screen.queryText(/cannot be undone/)).toBeNull();
    expect(served()).toEqual([]);
  });

  it("asks about one booking at a time", async () => {
    const two = "BK-2026-000002";
    mockApi.also({
      [DAY_PATH]: () =>
        json(200, [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ bookingNo: NO, donorName: "First" }), calendarBooking({ bookingNo: two, donorName: "Second" })] })]),
    });
    const screen = await render(<TodayView />);

    await press(screen.allMatching("Mark as served")[1]);

    screen.getText("Has Second given this meal? This cannot be undone.");
    expect(screen.queryText("Has First given this meal? This cannot be undone.")).toBeNull();
  });

  it("marks the booking on the second tap, says so, and shows it served", async () => {
    const screen = await render(<TodayView />);
    await ask(screen);

    mockApi.also({ [DAY_PATH]: () => json(200, [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ bookingNo: NO, status: "Completed" })] })]) });
    await press(screen.getText("Yes, served"));

    expect(served()).toEqual([{ method: "POST", path: `/api/institution/bookings/${NO}/complete`, body: null, signedIn: true }]);
    screen.getText(`${NO} marked as served.`);
    screen.getText("Served");
    expect(screen.queryText("Mark as served")).toBeNull();
    expect(screen.queryText(/cannot be undone/)).toBeNull();
  });

  it("closes the question even if the day cannot be read afterwards", async () => {
    const screen = await render(<TodayView />);
    await ask(screen);

    mockApi.also({ [TODAY_PATH]: () => Promise.reject(new Error("offline")) });
    await press(screen.getText("Yes, served"));

    screen.getText(`${NO} marked as served.`);
    expect(screen.queryText(/cannot be undone/)).toBeNull();
  });

  it("clears an earlier refusal as soon as the next try starts", async () => {
    mockApi.also({ [SERVE_PATH]: () => Promise.reject(new Error("offline")) });
    const screen = await render(<TodayView />);
    await ask(screen);
    await press(screen.getText("Yes, served"));
    screen.getText("Can't reach Sethika right now. Check your connection and try again.");

    mockApi.also({ [SERVE_PATH]: () => new Promise(() => {}) });
    await press(screen.getText("Yes, served"));

    expect(screen.queryText("Can't reach Sethika right now. Check your connection and try again.")).toBeNull();
  });

  it("cannot be sent twice while the first is on its way", async () => {
    let answer: (response: Response) => void = () => {};
    mockApi.also({ [SERVE_PATH]: () => new Promise((resolve) => (answer = resolve)) });
    const screen = await render(<TodayView />);
    await ask(screen);

    const yes = screen.getText("Yes, served");
    await press(yes);
    await press(yes);
    expect(isDisabled(screen.getText("Back"))).toBe(true);

    await act(async () => answer(serveOk()));
    await settle();
    expect(served()).toHaveLength(1);
  });

  it("keeps the question open after a lost connection, to try again", async () => {
    mockApi.also({ [SERVE_PATH]: () => Promise.reject(new Error("offline")) });
    const screen = await render(<TodayView />);
    await ask(screen);
    const reads = mockApi.sent.length;

    await press(screen.getText("Yes, served"));

    screen.getText("Can't reach Sethika right now. Check your connection and try again.");
    screen.getText(/cannot be undone/);
    expect(mockApi.sent.length).toBe(reads + 1);
    expect(isDisabled(screen.getText("Yes, served"))).toBe(false);
  });

  it("clears the refusal when the question is asked again", async () => {
    mockApi.also({ [SERVE_PATH]: () => json(409, { code: "too_early" }) });
    const screen = await render(<TodayView />);
    await ask(screen);
    await press(screen.getText("Yes, served"));
    screen.getText("This can only be marked on or after the day of the meal.");

    await press(screen.getText("Back"));
    await ask(screen);

    expect(screen.queryText("This can only be marked on or after the day of the meal.")).toBeNull();
  });

  it.each(["invalid_transition", "stale"])("reads the day again after %s, and says why", async (code) => {
    mockApi.also({ [SERVE_PATH]: () => json(409, { code }) });
    const screen = await render(<TodayView />);
    await ask(screen);

    mockApi.also({ [DAY_PATH]: () => json(200, [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ bookingNo: NO, status: "NoShow" })] })]) });
    await press(screen.getText("Yes, served"));

    screen.getText("Did not happen");
    screen.getText(/has already moved on|Someone else changed this/);
    expect(screen.queryText(/cannot be undone/)).toBeNull();
    expect(screen.queryText(`${NO} marked as served.`)).toBeNull();
  });

  it("says a 403 is the role's, not the booking's", async () => {
    mockApi.also({ [SERVE_PATH]: () => json(403, {}) });
    const screen = await render(<TodayView />);
    await ask(screen);

    await press(screen.getText("Yes, served"));

    screen.getText("Your role in this institution does not include this page.");
  });

  it("shows a refusal under its own booking only", async () => {
    const two = "BK-2026-000002";
    mockApi.also({
      [DAY_PATH]: () => json(200, [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ bookingNo: NO }), calendarBooking({ bookingNo: two })] })]),
      [SERVE_PATH]: () => json(409, { code: "too_early" }),
    });
    const screen = await render(<TodayView />);
    await press(screen.allMatching("Mark as served")[0]);

    await press(screen.getText("Yes, served"));

    const says = (id: string) =>
      screen.getTestId(id).queryAll((node) => node.type === "Text" && node.children.join("").startsWith("This can only")).length > 0;
    expect([says(`sponsor-${NO}`), says(`sponsor-${two}`)]).toEqual([true, false]);
  });

  it("escapes the booking number in the path", async () => {
    const odd = "BK/1";
    mockApi.also({
      [DAY_PATH]: () => json(200, [slotDay({ date: DATE, mealSlot: LUNCH, bookings: [calendarBooking({ bookingNo: odd })] })]),
      "POST /api/institution/bookings/BK%2F1/complete": serveOk,
    });
    const screen = await render(<TodayView />);
    await ask(screen);

    await press(screen.getText("Yes, served"));

    expect(served()[0].path).toBe("/api/institution/bookings/BK%2F1/complete");
  });
});
