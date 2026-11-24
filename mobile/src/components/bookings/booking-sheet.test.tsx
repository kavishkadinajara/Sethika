import type { SessionState } from "@/auth/session-client";
import type { PublicMealSlot } from "@/bookings/types";
import { createApiStub, DONOR } from "@/test-utils/api-stub";
import { day, institution, LUNCH, mealSlot } from "@/test-utils/fixtures";
import { json, press, render, type } from "@/test-utils/render";

import { BookingSheet } from "./booking-sheet";

/*  Booking one slot-day as a signed-in donor, against POST /api/bookings stubbed at the app's
    client boundary. The answers and codes are DonorBookingEndpoints.cs's (plan 03, E25).   */

const mockApi = createApiStub();
let mockSession: SessionState = DONOR;

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("expo-router", () => ({ router: { push: jest.fn(), navigate: jest.fn(), dismissTo: jest.fn() } }));
jest.mock("react-native-safe-area-context", () => jest.requireActual("react-native-safe-area-context/jest/mock").default);

const DAY = day("2026-11-21", { sponsors: 2, headcount: 30 });

function sheet(slot: PublicMealSlot = mealSlot()) {
  const handlers = { onClose: jest.fn(), onBooked: jest.fn(), onTaken: jest.fn(), onSignIn: jest.fn() };
  return { handlers, element: <BookingSheet institution={institution({ slots: [slot] })} slot={slot} day={DAY} {...handlers} /> };
}

beforeEach(() => {
  mockApi.sent.length = 0;
  mockSession = DONOR;
});

describe("the booking form", () => {
  it("books a slot that needs no approval, and says Booked with the number", async () => {
    mockApi.serve({ "POST /api/bookings": () => json(201, { bookingNo: "BK-2026-000123", status: "Approved" }) });
    const { element, handlers } = sheet(mealSlot({ requiresApproval: false }));
    const screen = await render(element);

    await type(screen.getLabel("How many people will be fed?"), "25");
    await press(screen.getText("The institution prepares it"));
    await type(screen.getLabel("In memory of (optional)"), "  Our grandmother ");
    await press(screen.getText("Book this day"));

    expect(mockApi.sent).toEqual([
      {
        method: "POST",
        path: "/api/bookings",
        signedIn: true,
        body: { mealSlot: LUNCH, date: "2026-11-21", headcount: 25, fulfilment: "InstitutionPrepares", inMemoryOf: "Our grandmother", notes: null },
      },
    ]);
    screen.getText("Booked");
    screen.getText("BK-2026-000123");
    screen.getText("We have texted you the details, and will remind you the evening before.");
    expect(handlers.onBooked).toHaveBeenCalled();
  });

  it("sends a request for a slot the institution approves, and says Request sent", async () => {
    mockApi.serve({ "POST /api/bookings": () => json(201, { bookingNo: "BK-2026-000124", status: "Requested" }) });
    const screen = await render(sheet(mealSlot({ requiresApproval: true })).element);

    screen.getText("They confirm each booking. We will text you their reply.");
    await press(screen.getText("Send request"));

    screen.getText("Request sent");
    screen.getText("We will text you when they reply. The day is held for you meanwhile.");
    expect(screen.queryText("Booked")).toBeNull();
  });

  it("keeps the headcount inside what the day allows before sending anything", async () => {
    const screen = await render(sheet().element);

    screen.getText("Between 10 and 30.");
    await type(screen.getLabel("How many people will be fed?"), "31");
    await press(screen.getText("Book this day"));

    screen.getText("Please enter a number from 10 to 30.");
    expect(mockApi.sent).toEqual([]);
  });

  it("estimates the cost from the slot's cost per head", async () => {
    const screen = await render(sheet(mealSlot({ estimatedCostPerHead: 450 })).element);
    await type(screen.getLabel("How many people will be fed?"), "25");
    screen.getText("Estimated cost: about LKR 11,250");
  });
});

describe("a refusal", () => {
  it("is said in the donor's words, in the sheet, when the donor can do something about it", async () => {
    mockApi.serve({ "POST /api/bookings": () => json(409, { code: "already_booked" }) });
    const { element, handlers } = sheet();
    const screen = await render(element);

    await press(screen.getText("Book this day"));

    screen.getText("You have already booked this meal on this day.");
    expect(handlers.onTaken).not.toHaveBeenCalled();
  });

  it("closes the sheet when the day itself has gone, with the reason", async () => {
    mockApi.serve({ "POST /api/bookings": () => json(409, { code: "slot_full" }) });
    const { element, handlers } = sheet();
    const screen = await render(element);

    await press(screen.getText("Book this day"));

    expect(handlers.onTaken).toHaveBeenCalledWith("Someone has just taken this day. Please choose another.");
  });

  it("says a lost connection as one", async () => {
    mockApi.serve({ "POST /api/bookings": () => Promise.reject(new TypeError("Network request failed")) });
    const screen = await render(sheet().element);

    await press(screen.getText("Book this day"));

    screen.getText("Can't reach Sethika right now. Check your connection and try again.");
  });
});

describe("the gate", () => {
  it("asks a visitor to sign in or sign up, and never shows the form", async () => {
    mockSession = { status: "anonymous" };
    const { element, handlers } = sheet();
    const screen = await render(element);

    expect(screen.queryText("Book this day")).toBeNull();
    await press(screen.getText("Create an account"));
    expect(handlers.onSignIn).toHaveBeenCalledWith("/sign-up");
  });

  it("does not book from a session acting for an institution", async () => {
    mockSession = { ...DONOR, status: "authenticated", activeInstitution: "test-elders-home" } as SessionState;
    const screen = await render(sheet().element);

    screen.getText(/^You are acting for an institution\. To book as yourself, change to your own account in Profile\.$/);
    expect(screen.queryText("Book this day")).toBeNull();
  });
});
