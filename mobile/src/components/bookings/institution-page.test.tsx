import { router } from "expo-router";

import type { SessionState } from "@/auth/session-client";
import { ANONYMOUS, createApiStub, DONOR } from "@/test-utils/api-stub";
import { day, institution, LUNCH, month } from "@/test-utils/fixtures";
import { isDisabled, json, press, render } from "@/test-utils/render";

import { InstitutionPage } from "./institution-page";

/*  The institution page against the donor API, stubbed at the app's own client boundary.
    Today is 2026-11-05 in Colombo; the slot books 2 to 90 days ahead.                     */

const mockApi = createApiStub();
let mockSession: SessionState = ANONYMOUS;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("expo-router", () => ({ router: { push: jest.fn(), navigate: jest.fn(), dismissTo: jest.fn() } }));
jest.mock("react-native-safe-area-context", () => jest.requireActual("react-native-safe-area-context/jest/mock").default);
jest.mock("react-native-webview", () => ({ WebView: () => null }));

const SLUG = "test-elders-home";
const NOVEMBER = month("2026-11", LUNCH, [
  ...["01", "02", "03", "04", "05", "06"].map((d) => day(`2026-11-${d}`, { bookable: false, reason: "too_soon" })),
  day("2026-11-20", { bookable: false, reason: "slot_full", sponsors: 0 }),
  day("2026-11-21", { sponsors: 1 }),
  day("2026-11-25", { bookable: false, reason: "date_blocked" }),
]);

beforeEach(() => {
  jest.useFakeTimers({ advanceTimers: true, now: new Date("2026-11-05T04:00:00Z") });
  jest.mocked(router.push).mockClear();
  mockApi.sent.length = 0;
  mockSession = ANONYMOUS;
  mockApi.serve({
    [`GET /api/institutions/${SLUG}`]: () => json(200, institution()),
    [`GET /api/institutions/${SLUG}/availability?month=2026-11`]: () => json(200, NOVEMBER),
    [`GET /api/institutions/${SLUG}/availability?month=2026-12`]: () => json(200, month("2026-12", LUNCH)),
  });
});

afterEach(() => {
  jest.useRealTimers();
});

describe("browsing, signed out", () => {
  it("shows the institution, its meal and the month as bowls in words", async () => {
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);

    screen.getText("Test Elders' Home");
    screen.getText("Lunch");
    screen.getText("November 2026");
    expect(screen.getLabel(/^Friday, 20 November 2026\./).props.accessibilityLabel).toBe("Friday, 20 November 2026. Full");
    expect(screen.getLabel(/^Saturday, 21 November 2026\./).props.accessibilityLabel).toBe("Saturday, 21 November 2026. 1 place left");
    expect(screen.getLabel(/^Sunday, 22 November 2026\./).props.accessibilityLabel).toBe("Sunday, 22 November 2026. 4 places left");
    expect(screen.getLabel(/^Wednesday, 25 November 2026\./).props.accessibilityLabel).toBe("Wednesday, 25 November 2026. Closed this day");
    expect(screen.getLabel(/^Thursday, 5 November 2026\./).props.accessibilityLabel).toBe("Thursday, 5 November 2026. Too soon to book");
    // Every request was anonymous.
    expect(mockApi.sent.every((request) => !request.signedIn)).toBe(true);
  });

  it("says why a day that is not open cannot be chosen, and opens no sheet", async () => {
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);

    await press(screen.getTestId("day-2026-11-20"));

    screen.getText("Friday, 20 November 2026: Full");
    expect(screen.queryText("Sponsor Lunch")).toBeNull();
  });

  it("pages months only inside the slot's window", async () => {
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);

    expect(isDisabled(screen.getLabel("Previous month"))).toBe(true);
    expect(isDisabled(screen.getLabel("Next month"))).toBe(false);

    await press(screen.getLabel("Next month"));

    screen.getText("December 2026");
    expect(mockApi.sent.map((request) => request.path)).toContain(`/api/institutions/${SLUG}/availability?month=2026-12`);
    expect(isDisabled(screen.getLabel("Previous month"))).toBe(false);
  });

  it("asks for a sign-in only when a day is chosen, and sends the donor back to that day", async () => {
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);

    await press(screen.getTestId("day-2026-11-21"));
    screen.getText("Sponsor Lunch");
    screen.getText("Sign in to book this day. It takes a minute, and we will bring you straight back here.");

    await press(screen.getText("Sign in"));

    expect(router.push).toHaveBeenCalledWith({
      pathname: "/sign-in",
      params: { next: `/i/${SLUG}?slot=${LUNCH}&date=2026-11-21` },
    });
    expect(screen.queryText("Sponsor Lunch")).toBeNull();
  });

  it("says plainly when the institution does not exist", async () => {
    mockApi.serve({ [`GET /api/institutions/${SLUG}`]: () => json(404, { code: "institution_not_found" }) });
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);
    screen.getText("We could not find this place. The link may be old.");
  });

  it("offers a retry when the page cannot load", async () => {
    mockApi.serve({ [`GET /api/institutions/${SLUG}`]: () => Promise.reject(new TypeError("Network request failed")) });
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);
    screen.getText("Can't reach Sethika right now. Check your connection and try again.");

    mockApi.also({ [`GET /api/institutions/${SLUG}`]: () => json(200, institution()) });
    await press(screen.getText("Try again"));
    screen.getText("Test Elders' Home");
  });
});

describe("back from signing in", () => {
  it("opens the sheet again on the day the donor left it for, once the session starts", async () => {
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);
    await press(screen.getTestId("day-2026-11-21"));
    await press(screen.getText("Sign in"));
    expect(screen.queryText("Book this day")).toBeNull();

    // The sign-in screen started a session and went back down the stack to this page.
    mockSession = DONOR;
    await screen.rerender(<InstitutionPage slug={SLUG} chosen={null} />);

    screen.getText(/^Saturday, 21 November 2026 · 11:30–13:00/);
    screen.getText("Book this day");
  });

  it("opens the booking form on the day the route names", async () => {
    mockSession = DONOR;
    const screen = await render(<InstitutionPage slug={SLUG} chosen={{ slot: LUNCH, date: "2026-11-21" }} />);

    screen.getText("Sponsor Lunch");
    screen.getText(/^Saturday, 21 November 2026 · 11:30–13:00/);
    screen.getText("Book this day");
  });

  it("does not open a day that has gone in the meantime, and says why", async () => {
    mockSession = DONOR;
    const screen = await render(<InstitutionPage slug={SLUG} chosen={{ slot: LUNCH, date: "2026-11-20" }} />);

    expect(screen.queryText("Sponsor Lunch")).toBeNull();
    screen.getText("Friday, 20 November 2026: Full");
  });
});

describe("giving online (M8, M10)", () => {
  it("offers it only where the institution takes gifts online", async () => {
    const without = await render(<InstitutionPage slug={SLUG} chosen={null} />);
    expect(without.queryText("Give online")).toBeNull();
    without.unmount();

    mockApi.also({ [`GET /api/institutions/${SLUG}`]: () => json(200, institution({ givesOnline: true })) });
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);
    await press(screen.getText("Give online"));
    screen.getText("Give to Test Elders' Home");
  });

  it("sends a visitor to sign in and back to the give sheet", async () => {
    mockApi.also({ [`GET /api/institutions/${SLUG}`]: () => json(200, institution({ givesOnline: true })) });
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} />);
    await press(screen.getText("Give online"));
    await press(screen.getText("Sign in"));

    expect(router.push).toHaveBeenCalledWith({ pathname: "/sign-in", params: { next: `/i/${SLUG}?give=1` } });
  });

  it("opens the give sheet when the route asks for it, for a signed-in donor", async () => {
    mockSession = DONOR;
    mockApi.also({ [`GET /api/institutions/${SLUG}`]: () => json(200, institution({ givesOnline: true })) });
    const screen = await render(<InstitutionPage slug={SLUG} chosen={null} give />);

    screen.getText("Give to Test Elders' Home");
    screen.getText("Go to PayHere");
  });
});
