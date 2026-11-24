import type { SessionState } from "@/auth/session-client";
import { ANONYMOUS, DONOR, STAFF } from "@/test-utils/api-stub";
import { calendarBooking, slotDay, tableSlot } from "@/test-utils/fixtures";

import { countRequests, failureKey, institutionSentence, mayOpenToday, mayServe, movedOn, todayMeals, WAITING_DAYS } from "./today";
import type { TodaysTable } from "./types";

/*  The Today tab's rules. The sentence's cases are the web's (lib/dashboard/sentence.test.ts). */

const DATE = "2026-11-10";
const table = (...slots: ReturnType<typeof tableSlot>[]): TodaysTable => ({ date: DATE, slots });
const all = () => true;

describe("mayOpenToday", () => {
  it("is staff of an institution whose role reads the calendar", () => {
    expect(mayOpenToday(STAFF, (code) => code === "BOOKINGS.CALENDAR.VIEW")).toBe(true);
  });

  it("is not a role without the calendar, whatever else it holds", () => {
    expect(mayOpenToday(STAFF, (code) => code !== "BOOKINGS.CALENDAR.VIEW")).toBe(false);
  });

  it.each<[string, SessionState]>([
    ["a visitor", ANONYMOUS],
    ["someone on their own account, even with the permission", DONOR],
    ["a session still loading", { status: "loading" }],
  ])("is not %s", (_, session) => {
    expect(mayOpenToday(session, all)).toBe(false);
  });
});

describe("mayServe", () => {
  it("is a confirmed booking, for a role that edits the calendar", () => {
    expect(mayServe(calendarBooking({ status: "Approved" }), (code) => code === "BOOKINGS.CALENDAR.EDIT")).toBe(true);
  });

  it("is nothing for a role that only reads it", () => {
    expect(mayServe(calendarBooking({ status: "Approved" }), (code) => code === "BOOKINGS.CALENDAR.VIEW")).toBe(false);
  });

  it.each(["Requested", "Rejected", "Cancelled", "Completed", "NoShow"] as const)("is not a booking that is %s", (status) => {
    expect(mayServe(calendarBooking({ status }), all)).toBe(false);
  });
});

describe("todayMeals", () => {
  it("lays every meal of the table, booked or not, with its sponsors", () => {
    const lunch = tableSlot({ mealSlot: "lunch", startTime: "11:30" });
    const dinner = tableSlot({ mealSlot: "dinner", startTime: "18:00" });
    const taken = calendarBooking({ bookingNo: "BK-1" });

    const meals = todayMeals(table(lunch, dinner), [slotDay({ date: DATE, mealSlot: "dinner", bookings: [taken] })]);

    expect(meals.map((meal) => [meal.mealSlot, meal.sponsors.map((b) => b.bookingNo)])).toEqual([
      ["lunch", []],
      ["dinner", ["BK-1"]],
    ]);
  });

  it("leaves out whoever gave their place back, and keeps everyone else", () => {
    const bookings = (["Requested", "Approved", "Rejected", "Cancelled", "Completed", "NoShow"] as const).map((status) =>
      calendarBooking({ bookingNo: status, status }),
    );

    const [meal] = todayMeals(table(tableSlot({ mealSlot: "lunch" })), [slotDay({ date: DATE, mealSlot: "lunch", bookings })]);

    expect(meal.sponsors.map((b) => b.status)).toEqual(["Requested", "Approved", "Completed", "NoShow"]);
  });

  it("brings in a meal switched off after it was booked, in time order, with what the calendar knows", () => {
    const lunch = tableSlot({ mealSlot: "lunch", startTime: "11:30" });
    const dinner = tableSlot({ mealSlot: "dinner", startTime: "18:00" });
    const off = slotDay({
      date: DATE,
      mealSlot: "breakfast",
      slotName: "Breakfast",
      slotNameSi: "උදෑසන",
      startTime: "07:00",
      maxSponsors: 3,
      bookedSponsors: 2,
      status: "Full",
      bookings: [calendarBooking({ status: "Requested" }), calendarBooking({ bookingNo: "BK-2", status: "Approved" })],
    });

    const meals = todayMeals(table(lunch, dinner), [off]);

    expect(meals.map((meal) => meal.mealSlot)).toEqual(["breakfast", "lunch", "dinner"]);
    expect(meals[0]).toMatchObject({ name: "Breakfast", nameSi: "උදෑසන", maxSponsors: 3, bookedSponsors: 2, requested: 1, state: "Full" });
  });

  it("does not bring in an off-table meal nobody stands behind any more", () => {
    const gone = slotDay({ date: DATE, mealSlot: "breakfast", bookings: [calendarBooking({ status: "Cancelled" })] });

    expect(todayMeals(table(), [gone])).toEqual([]);
  });

  it("reads only today's slot-days", () => {
    const tomorrow = slotDay({ date: "2026-11-11", mealSlot: "lunch", bookings: [calendarBooking()] });

    const meals = todayMeals(table(tableSlot({ mealSlot: "lunch" })), [tomorrow]);

    expect(meals[0].sponsors).toEqual([]);
    expect(todayMeals(table(), [tomorrow])).toEqual([]);
  });
});

describe("countRequests", () => {
  it("adds up the requests across days, and nothing else", () => {
    const requested = calendarBooking({ status: "Requested" });
    const days = [slotDay({ bookings: [requested, calendarBooking({ status: "Approved" })] }), slotDay({ bookings: [requested] })];

    expect(countRequests(days)).toBe(2);
  });

  it("reads as far as the API's widest range allows", () => {
    expect(WAITING_DAYS).toBe(62);
  });
});

describe("institutionSentence", () => {
  const lunch = tableSlot({ name: "Lunch", nameSi: "දිවා ආහාරය", startTime: "11:30", maxSponsors: 4, bookedSponsors: 1, state: "Open" });
  const dinner = tableSlot({ name: "Dinner", startTime: "18:00", maxSponsors: 2, bookedSponsors: 2, state: "Full" });
  const say = (slots: ReturnType<typeof tableSlot>[], waiting: number | null, now: string, language: "en" | "si" = "en") =>
    institutionSentence({ table: table(...slots), waiting, language, now });

  it("puts requests first, with the next meal", () => {
    expect(say([lunch], 3, "09:00")).toEqual({
      key: "Dashboard.sentence.waitingAndNext",
      values: { count: 3, meal: "Lunch", time: "11:30" },
    });
  });

  it("says only the requests when no meal is left", () => {
    expect(say([lunch], 1, "12:00")).toEqual({ key: "Dashboard.sentence.waiting", values: { count: 1 } });
  });

  it("names the meal in the screen's language", () => {
    expect(say([lunch], null, "09:00", "si").values.meal).toBe("දිවා ආහාරය");
  });

  it("counts the places open on the meals yet to come", () => {
    const later = tableSlot({ startTime: "13:00", maxSponsors: 5, bookedSponsors: 3, state: "Open" });

    expect(say([lunch, later], null, "09:00")).toEqual({
      key: "Dashboard.sentence.nextWithOpen",
      values: { meal: "Lunch", time: "11:30", open: 5 },
    });
  });

  it("does not count a meal already begun, or one that is full", () => {
    const later = tableSlot({ name: "Tea", startTime: "15:00", maxSponsors: 5, bookedSponsors: 4, state: "Open" });

    expect(say([lunch, later, dinner], 0, "12:00")).toEqual({
      key: "Dashboard.sentence.nextWithOpen",
      values: { meal: "Tea", time: "15:00", open: 1 },
    });
  });

  it("does not count the places left on a meal closed to more", () => {
    const closedToMore = tableSlot({ startTime: "13:00", maxSponsors: 5, bookedSponsors: 3, state: "Full" });

    expect(say([closedToMore], null, "09:00").key).toBe("Dashboard.sentence.nextAllGiven");
  });

  it("says every meal to come has its sponsor", () => {
    expect(say([dinner], null, "09:00")).toEqual({
      key: "Dashboard.sentence.nextAllGiven",
      values: { meal: "Dinner", time: "18:00" },
    });
  });

  it("says when the meals have all begun; a meal starting this minute has begun", () => {
    expect(say([lunch], null, "11:30")).toEqual({ key: "Dashboard.sentence.mealsOver", values: {} });
  });

  it("says when there are no meals, and a closed one is not a meal", () => {
    expect(say([], null, "09:00").key).toBe("Dashboard.sentence.noMeals");
    expect(say([tableSlot({ state: "Closed" })], null, "09:00").key).toBe("Dashboard.sentence.noMeals");
  });

  it("does not offer a closed meal as next", () => {
    const closed = tableSlot({ name: "Breakfast", startTime: "08:00", state: "Closed" });

    expect(say([closed, lunch], 2, "07:00").values.meal).toBe("Lunch");
  });
});

describe("failures", () => {
  const failure = (status: number, code: string | null) => ({ status, code, fields: [] });

  it("says a 403 is this role, and leaves the rest to the booking words", () => {
    expect(failureKey(failure(403, "no_institution"))).toBe("Booking.gate.notAllowed");
    expect(failureKey(failure(409, "too_early"))).toBe("Booking.errors.too_early");
    expect(failureKey(failure(0, null))).toBe("Errors.network");
  });

  it.each(["stale", "invalid_transition", "booking_not_found"])("reads the list again after %s", (code) => {
    expect(movedOn(failure(409, code))).toBe(true);
  });

  it("keeps the list after a lost connection or a refusal that changes nothing", () => {
    expect(movedOn(failure(0, null))).toBe(false);
    expect(movedOn(failure(409, "too_early"))).toBe(false);
    expect(movedOn(failure(403, null))).toBe(false);
  });
});
