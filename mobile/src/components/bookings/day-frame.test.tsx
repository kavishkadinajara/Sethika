import { router } from "expo-router";
import { act } from "react";
import { AppState, StyleSheet } from "react-native";

import type { SessionState } from "@/auth/session-client";
import { OWN_BOOKINGS_VIEW } from "@/bookings/donor-day";
import { I18nProvider } from "@/i18n/i18n-provider";
import { ANONYMOUS, createApiStub, DONOR, STAFF } from "@/test-utils/api-stub";
import { booking } from "@/test-utils/fixtures";
import { json, press, render, settle } from "@/test-utils/render";

import { DayFrame } from "./day-frame";

/*  The top of Explore against GET /api/bookings/mine, stubbed at the app's client boundary. It is
    09:30 on 2026-11-10 in Colombo, a Tuesday.                                                   */

const mockApi = createApiStub();
let mockSession: SessionState = DONOR;
let mockPermissions: { ready: boolean; can: (code: string) => boolean };
let mockProfile: { fullName: string; displayName: string | null; preferredLanguage: string | null } | null = null;
let mockFocus: (() => void) | null = null;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("@/auth/permission-provider", () => ({ usePermissions: () => mockPermissions }));
jest.mock("@/auth/profile-provider", () => ({
  useProfile: () => mockProfile,
  greetingName: (profile: { displayName: string | null } | null) => profile?.displayName ?? null,
}));
jest.mock("expo-localization", () => ({ getLocales: () => [{ languageCode: mockLanguage }] }));
jest.mock("expo-router", () => {
  const react = jest.requireActual<typeof import("react")>("react");
  return {
    router: { push: jest.fn(), navigate: jest.fn() },
    // The screen coming back into view runs the effect again, as a real focus does.
    useFocusEffect: (effect: () => void | (() => void)) => {
      const [focuses, setFocuses] = react.useState(0);
      react.useEffect(() => {
        mockFocus = () => setFocuses((count) => count + 1);
      }, []);
      react.useEffect(effect, [effect, focuses]);
    },
  };
});

let mockLanguage = "en";
let phone: ((state: string) => void)[] = [];

const MINE = "GET /api/bookings/mine";
const reads = () => mockApi.sent.filter((request) => request.path === "/api/bookings/mine").length;
const inFrame = (ui = <DayFrame />) => <I18nProvider>{ui}</I18nProvider>;

beforeEach(() => {
  jest.useFakeTimers({ advanceTimers: true, now: new Date("2026-11-10T04:00:00Z") });
  mockApi.sent.length = 0;
  mockSession = DONOR;
  mockLanguage = "en";
  mockProfile = null;
  mockPermissions = { ready: true, can: (code) => code === OWN_BOOKINGS_VIEW };
  mockApi.serve({ [MINE]: () => json(200, []) });
  jest.mocked(router.push).mockClear();
  jest.mocked(router.navigate).mockClear();
  phone = [];
  jest.spyOn(AppState, "addEventListener").mockImplementation((_, listener) => {
    phone.push(listener as (state: string) => void);
    return { remove: () => {} } as never;
  });
});

afterEach(() => {
  jest.restoreAllMocks();
  jest.useRealTimers();
});

describe("the date", () => {
  it("is the weekday over the day and month, and heard as one header with the year", async () => {
    const screen = await render(inFrame());
    screen.getText("Tuesday");
    screen.getText("10 November");
    expect(screen.getLabel("Tuesday, 10 November 2026").props.accessibilityRole).toBe("header");
  });

  it("is in Colombo, past midnight there while it is still yesterday in UTC", async () => {
    jest.setSystemTime(new Date("2026-11-10T18:45:00Z"));
    const screen = await render(inFrame());
    screen.getText("11 November");
  });

  it("is in the order Sinhala writes it, at the leading its vowel signs need", async () => {
    mockLanguage = "si";
    const screen = await render(inFrame());
    const line = screen.getText("නොවැම්බර් 10");
    expect(StyleSheet.flatten(line.props.style).lineHeight).toBe(73);
  });

  it("turns over at midnight with the app left open", async () => {
    jest.setSystemTime(new Date("2026-11-10T18:29:45Z"));
    const screen = await render(inFrame());
    screen.getText("10 November");

    await act(async () => {
      jest.advanceTimersByTime(30_000);
    });

    screen.getText("11 November");
  });

  it("stops its clock when it leaves the screen", async () => {
    mockSession = ANONYMOUS;
    const started = jest.spyOn(globalThis, "setInterval");
    const stopped = jest.spyOn(globalThis, "clearInterval");
    const screen = await render(inFrame());
    const clock = started.mock.results.at(-1)?.value;
    expect(clock).toBeDefined();
    expect(stopped).not.toHaveBeenCalledWith(clock);

    await act(async () => screen.unmount());

    expect(stopped).toHaveBeenCalledWith(clock);
  });

  it("is right at once when the phone comes back to the app", async () => {
    const screen = await render(inFrame());

    // Timers stop in the background, so only the return can move the clock on.
    jest.setSystemTime(new Date("2026-11-11T02:00:00Z"));
    await act(async () => {
      phone.forEach((change) => change("background"));
    });
    screen.getText("10 November");

    await act(async () => {
      phone.forEach((change) => change("active"));
    });
    screen.getText("11 November");
  });
});

describe("a visitor", () => {
  it("gets the date and the invitation, and no read of anyone's bookings", async () => {
    mockSession = ANONYMOUS;
    mockPermissions = { ready: false, can: () => false };

    const screen = await render(inFrame());

    screen.getText("10 November");
    screen.getText("Find a place, and give a meal on a day that matters to you.");
    expect(screen.queryText("Your table")).toBeNull();
    expect(reads()).toBe(0);
  });

  it("sees a quiet bar, not words, while the session is still being found", async () => {
    mockSession = { status: "loading" };
    const screen = await render(inFrame());
    screen.getTestId("sentence-waiting");
    expect(screen.queryText(/Find a place/)).toBeNull();
    expect(reads()).toBe(0);
  });
});

describe("who is not read for", () => {
  it("asks nothing for a session acting for an institution", async () => {
    mockSession = STAFF;
    const screen = await render(inFrame());
    screen.getText(/^Find a place/);
    expect(reads()).toBe(0);
  });

  it("asks nothing of an account that may not read bookings", async () => {
    mockPermissions = { ready: true, can: () => false };
    const screen = await render(inFrame());
    screen.getText(/^Find a place/);
    expect(reads()).toBe(0);
  });

  it("waits for the permissions before it says or asks anything", async () => {
    mockPermissions = { ready: false, can: () => false };
    const screen = await render(inFrame());
    screen.getTestId("sentence-waiting");
    expect(reads()).toBe(0);
  });
});

describe("a donor", () => {
  it("greets them by name, and a visitor by nothing", async () => {
    mockProfile = { fullName: "Nimal Perera", displayName: "Nimal", preferredLanguage: "en" };
    const screen = await render(inFrame());
    screen.getText("Welcome back, Nimal");
  });

  it("says their next meal, with a far date in words and never as stored", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20", status: "Approved" })]) });
    const screen = await render(inFrame());

    screen.getText("Your next meal to give is Lunch at Test Elders' Home, on Friday, 20 November 2026 at 11:30.");
    expect(screen.allText().join(" ")).not.toContain("2026-11-20");
  });

  it("shows a quiet bar until the read is back, and no claim about their bookings", async () => {
    let answer: (response: Response) => void = () => {};
    mockApi.serve({ [MINE]: () => new Promise<Response>((resolve) => (answer = resolve)) });
    const screen = await render(inFrame());

    screen.getTestId("sentence-waiting");
    expect(screen.queryText(/no meal booked/)).toBeNull();

    await act(async () => answer(json(200, [])));
    await settle();

    expect(screen.queryTestId("sentence-waiting")).toBeNull();
    screen.getText(/You have no meal booked yet/);
  });

  it("leaves out Your table when nothing is coming", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-01", status: "Completed" })]) });
    const screen = await render(inFrame());
    screen.getText(/You have no meal booked yet/);
    expect(screen.queryText("Your table")).toBeNull();
  });

  it("lists the next three meals, soonest first", async () => {
    const list = ["23", "21", "24", "22"].map((day) => booking({ bookingNo: `BK-${day}`, date: `2026-11-${day}`, slotName: `Meal ${day}` }));
    mockApi.serve({ [MINE]: () => json(200, list) });

    const screen = await render(inFrame());

    screen.getText("Your table");
    expect(screen.allText().filter((line) => line.startsWith("Meal "))).toEqual(["Meal 21", "Meal 22", "Meal 23"]);
  });

  it("puts each meal's day, time, place and status on its line, and says them aloud", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20", status: "Requested" })]) });
    const screen = await render(inFrame());

    screen.getText("Fri, 20 Nov");
    screen.getText("11:30");
    screen.getText("Test Elders' Home");
    screen.getText("Waiting for reply");
    screen.getLabel("Fri, 20 Nov, 11:30. Lunch, Test Elders' Home. Waiting for reply");
  });

  it("rules between the lines, and not above the first", async () => {
    const list = ["21", "22"].map((day) => booking({ bookingNo: `BK-${day}`, date: `2026-11-${day}` }));
    mockApi.serve({ [MINE]: () => json(200, list) });
    const screen = await render(inFrame());

    const rule = (label: RegExp) => {
      const { style } = screen.getLabel(label).props as { style: unknown };
      const flat = StyleSheet.flatten(typeof style === "function" ? style({ pressed: false }) : style) as { borderTopWidth?: number };
      return flat.borderTopWidth ?? 0;
    };
    expect(rule(/^Sat, 21 Nov/)).toBe(0);
    expect(rule(/^Sun, 22 Nov/)).toBe(1);
  });

  it("opens the place from a line, and all the bookings from the link under them", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20" })]) });
    const screen = await render(inFrame());

    await press(screen.getText("Lunch"));
    expect(router.push).toHaveBeenCalledWith({ pathname: "/i/[slug]", params: { slug: "test-elders-home" } });

    await press(screen.getText("All my bookings"));
    expect(router.navigate).toHaveBeenCalledWith("/bookings");
  });

  it("drops a meal from the table and the sentence once it begins, with no new read", async () => {
    jest.setSystemTime(new Date("2026-11-10T05:59:45Z"));
    const list = [
      booking({ bookingNo: "BK-1", date: "2026-11-10", startTime: "11:30", slotName: "Lunch" }),
      booking({ bookingNo: "BK-2", date: "2026-11-11", startTime: "18:00", slotName: "Dinner" }),
    ];
    mockApi.serve({ [MINE]: () => json(200, list) });
    const screen = await render(inFrame());
    screen.getText(/^Today you give Lunch/);

    await act(async () => {
      jest.advanceTimersByTime(30_000);
    });

    screen.getText(/^Tomorrow you give Dinner/);
    expect(screen.queryText("Lunch")).toBeNull();
    expect(reads()).toBe(1);
  });
});

describe("reading again", () => {
  it("reads when the tab comes back into view, so a booking just made shows", async () => {
    const screen = await render(inFrame());
    screen.getText(/You have no meal booked yet/);

    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20" })]) });
    await act(async () => mockFocus?.());
    await settle();

    screen.getText("Your table");
    expect(reads()).toBe(2);
  });

  it("reads on a pull", async () => {
    const screen = await render(inFrame(<DayFrame refreshKey={0} />));
    await screen.rerender(inFrame(<DayFrame refreshKey={1} />));
    await settle();
    expect(reads()).toBe(2);
  });

  it("does not read again for a new render alone", async () => {
    const screen = await render(inFrame());
    await screen.rerender(inFrame());
    await settle();
    expect(reads()).toBe(1);
  });

  it("invites when the first read fails, and never says nothing is booked", async () => {
    mockApi.serve({ [MINE]: () => json(500, {}) });
    const screen = await render(inFrame());

    screen.getText(/^Find a place/);
    expect(screen.queryText(/no meal booked/)).toBeNull();
    expect(screen.queryTestId("sentence-waiting")).toBeNull();
  });

  it("recovers from a failed read on the next one", async () => {
    mockApi.serve({ [MINE]: () => json(500, {}) });
    const screen = await render(inFrame());

    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20" })]) });
    await act(async () => mockFocus?.());
    await settle();

    screen.getText(/^Your next meal to give/);
  });

  it("keeps what it read when a later read fails", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20" })]) });
    const screen = await render(inFrame());

    mockApi.serve({ [MINE]: () => json(500, {}) });
    await act(async () => mockFocus?.());
    await settle();

    screen.getText(/^Your next meal to give/);
    screen.getText("Your table");
  });

  it("lets an earlier, slower read land on nothing", async () => {
    let first: (response: Response) => void = () => {};
    mockApi.serve({ [MINE]: () => new Promise<Response>((resolve) => (first = resolve)) });
    const screen = await render(inFrame(<DayFrame refreshKey={0} />));

    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20", slotName: "Newer" })]) });
    await screen.rerender(inFrame(<DayFrame refreshKey={1} />));
    await settle();
    screen.getText("Newer");

    await act(async () => first(json(200, [booking({ date: "2026-11-21", slotName: "Older" })])));
    await settle();

    screen.getText("Newer");
    expect(screen.queryText("Older")).toBeNull();
  });
});

describe("the session changing", () => {
  it("forgets the donor's meals at a sign-out, and the next account never sees them", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20", slotName: "Private meal" })]) });
    const screen = await render(inFrame());
    screen.getText("Private meal");

    mockSession = ANONYMOUS;
    await screen.rerender(inFrame());
    expect(screen.queryText("Private meal")).toBeNull();
    expect(screen.queryText("Your table")).toBeNull();

    let answer: (response: Response) => void = () => {};
    mockApi.serve({ [MINE]: () => new Promise<Response>((resolve) => (answer = resolve)) });
    mockSession = DONOR;
    await screen.rerender(inFrame());

    expect(screen.queryText("Private meal")).toBeNull();
    screen.getTestId("sentence-waiting");
    await act(async () => answer(json(200, [])));
  });

  it("forgets them when the session moves to an institution", async () => {
    mockApi.serve({ [MINE]: () => json(200, [booking({ date: "2026-11-20", slotName: "Private meal" })]) });
    const screen = await render(inFrame());

    mockSession = STAFF;
    await screen.rerender(inFrame());

    expect(screen.queryText("Private meal")).toBeNull();
    screen.getText(/^Find a place/);
  });
});
