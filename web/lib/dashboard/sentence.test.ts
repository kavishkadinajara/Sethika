import { describe, expect, it } from "vitest";
import type { DonorBooking, TableSlot, TodaysTable } from "@/lib/bookings/types";
import { colomboTime, donorSentence, institutionSentence, platformSentence, upcoming } from "./sentence";

const slot = (name: string, startTime: string, over: Partial<TableSlot> = {}): TableSlot => ({
  mealSlot: name,
  name,
  nameSi: `${name} si`,
  nameTa: null,
  startTime,
  endTime: startTime,
  maxSponsors: 1,
  bookedSponsors: 0,
  requested: 0,
  state: "Open",
  ...over,
});

const table = (...slots: TableSlot[]): TodaysTable => ({ date: "2026-11-02", slots });

describe("the institution's sentence", () => {
  const breakfast = slot("Breakfast", "07:00");
  const dana = slot("Dana", "11:00", { maxSponsors: 4, bookedSponsors: 1 });
  const dinner = slot("Dinner", "18:00", { state: "Full", bookedSponsors: 1 });

  it("puts requests to answer first, with the next meal", () => {
    expect(institutionSentence({ table: table(breakfast, dana), waiting: 2, locale: "en", now: "09:00" })).toEqual({
      key: "waitingAndNext",
      values: { count: 2, meal: "Dana", time: "11:00" },
    });
  });

  it("names only the requests once the day's meals have begun", () => {
    expect(institutionSentence({ table: table(breakfast), waiting: 1, locale: "en", now: "09:00" })).toEqual({ key: "waiting", values: { count: 1 } });
  });

  it("counts the places still open on meals yet to come, not on one begun", () => {
    // Breakfast has begun: its open place no longer counts. Dana has 3 left, dinner is full.
    expect(institutionSentence({ table: table(breakfast, dana, dinner), waiting: 0, locale: "en", now: "07:30" })).toEqual({
      key: "nextWithOpen",
      values: { meal: "Dana", time: "11:00", open: 3 },
    });
  });

  it("says every meal is given when no place is open", () => {
    expect(institutionSentence({ table: table(dinner), waiting: 0, locale: "en", now: "12:00" })).toEqual({
      key: "nextAllGiven",
      values: { meal: "Dinner", time: "18:00" },
    });
  });

  it("skips a closed meal, as if it were not laid", () => {
    const closed = slot("Closed lunch", "12:00", { state: "Closed" });
    expect(institutionSentence({ table: table(closed, dinner), waiting: 0, locale: "en", now: "10:00" }).values.meal).toBe("Dinner");
    expect(institutionSentence({ table: table(closed), waiting: 0, locale: "en", now: "10:00" }).key).toBe("noMeals");
  });

  it("knows a day with no meals from a day whose meals are over", () => {
    expect(institutionSentence({ table: table(), waiting: 0, locale: "en", now: "10:00" }).key).toBe("noMeals");
    expect(institutionSentence({ table: table(breakfast), waiting: 0, locale: "en", now: "20:00" }).key).toBe("mealsOver");
  });

  it("treats a meal at this very minute as begun", () => {
    expect(institutionSentence({ table: table(dana), waiting: 0, locale: "en", now: "11:00" }).key).toBe("mealsOver");
  });

  it("never names a number the session may not see", () => {
    expect(institutionSentence({ table: null, waiting: null, locale: "en", now: "10:00" })).toEqual({ key: "institutionPlain", values: {} });
    // A requests count of none is not the same as one the session may not see, but both go on to the table.
    expect(institutionSentence({ table: table(dana), waiting: null, locale: "en", now: "10:00" }).key).toBe("nextWithOpen");
  });

  it("names the meal in the page's language", () => {
    expect(institutionSentence({ table: table(dana), waiting: 0, locale: "si", now: "10:00" }).values.meal).toBe("Dana si");
    expect(institutionSentence({ table: table(dana), waiting: 0, locale: "ta", now: "10:00" }).values.meal).toBe("Dana");
  });
});

const booking = (bookingNo: string, date: string, startTime: string, over: Partial<DonorBooking> = {}): DonorBooking => ({
  bookingNo,
  status: "Approved",
  date,
  startTime,
  endTime: startTime,
  institution: "sri-bodhi",
  institutionName: "Sri Bodhi",
  institutionNameSi: "ශ්‍රී බෝධි",
  institutionNameTa: null,
  mealSlot: "m",
  slotName: "Dana",
  slotNameSi: null,
  slotNameTa: null,
  headcount: 10,
  fulfilmentMode: "DonorBrings",
  inMemoryOf: null,
  notes: null,
  createdAt: "2026-10-01T00:00:00Z",
  rejectReason: null,
  cancelReason: null,
  canCancel: true,
  yearly: null,
  ...over,
});

describe("the donor's sentence", () => {
  const today = "2026-11-02";

  it("names the soonest meal still to give, by day and then by time", () => {
    const bookings = [
      booking("BK-3", "2026-11-09", "07:00"),
      booking("BK-2", "2026-11-03", "18:00"),
      booking("BK-1", "2026-11-03", "11:00"),
    ];
    expect(upcoming(bookings, today, "10:00").map((b) => b.bookingNo)).toEqual(["BK-1", "BK-2", "BK-3"]);
    expect(donorSentence({ bookings, locale: "en", today, now: "10:00" })).toEqual({
      key: "givingTomorrow",
      values: { meal: "Dana", place: "Sri Bodhi", time: "11:00", date: "2026-11-03" },
    });
  });

  it("leaves out what is over or will not happen", () => {
    const bookings = [
      booking("BK-begun", today, "09:00"),
      booking("BK-rejected", today, "12:00", { status: "Rejected" }),
      booking("BK-cancelled", today, "12:00", { status: "Cancelled" }),
      booking("BK-done", "2026-11-01", "12:00", { status: "Completed" }),
      booking("BK-next", today, "18:00"),
    ];
    expect(upcoming(bookings, today, "10:00").map((b) => b.bookingNo)).toEqual(["BK-next"]);
    expect(donorSentence({ bookings, locale: "en", today, now: "10:00" }).key).toBe("givingToday");
  });

  it("says when the institution has not answered yet", () => {
    const asked = [booking("BK-1", "2026-11-20", "11:00", { status: "Requested" })];
    expect(donorSentence({ bookings: asked, locale: "si", today, now: "10:00" })).toEqual({
      key: "askedLater",
      values: { meal: "Dana", place: "ශ්‍රී බෝධි", time: "11:00", date: "2026-11-20" },
    });
  });

  it("invites a donor with nothing booked to find a place", () => {
    expect(donorSentence({ bookings: [], locale: "en", today, now: "10:00" }).key).toBe("nothingBooked");
    expect(donorSentence({ bookings: null, locale: "en", today, now: "10:00" }).key).toBe("donorPlain");
  });
});

describe("the platform's sentence", () => {
  it("names the queue and its oldest", () => {
    expect(platformSentence({ waiting: 3, oldestDays: 4 })).toEqual({ key: "queue", values: { count: 3, days: 4 } });
  });

  it("knows an empty queue from one the session may not see", () => {
    expect(platformSentence({ waiting: 0, oldestDays: null }).key).toBe("queueClear");
    expect(platformSentence({ waiting: null, oldestDays: null }).key).toBe("platformPlain");
  });
});

describe("Colombo's clock", () => {
  it("is UTC+05:30", () => {
    expect(colomboTime(new Date("2026-11-02T04:45:00Z"))).toBe("10:15");
    expect(colomboTime(new Date("2026-11-02T18:31:00Z"))).toBe("00:01");
  });
});
