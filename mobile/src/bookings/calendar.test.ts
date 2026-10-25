import {
  addDays,
  addMonths,
  colomboToday,
  dayOfWeek,
  daysBetween,
  isIsoDate,
  lastOfMonth,
  maskDays,
  monthGrid,
  monthWindow,
} from "./calendar";

/*  The calendar's arithmetic, on Colombo dates as strings (the API's own). The phone's time zone
    never enters: the tests run in whatever zone the machine has, and must pass in all of them. */

describe("colomboToday", () => {
  it("is Colombo's date, not UTC's: from 18:30 UTC it is already tomorrow there", () => {
    expect(colomboToday(new Date("2026-11-19T18:29:59Z"))).toBe("2026-11-19");
    expect(colomboToday(new Date("2026-11-19T18:30:00Z"))).toBe("2026-11-20");
    expect(colomboToday(new Date("2026-12-31T19:00:00Z"))).toBe("2027-01-01");
  });
});

describe("date arithmetic", () => {
  it("crosses month and year ends, and leap days", () => {
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addDays("2026-03-01", -1)).toBe("2026-02-28");
    expect(addMonths("2026-12", 1)).toBe("2027-01");
    expect(addMonths("2026-01", -1)).toBe("2025-12");
    expect(lastOfMonth("2028-02")).toBe("2028-02-29");
    expect(daysBetween("2026-12-30", "2027-01-02")).toBe(3);
    expect(daysBetween("2027-01-02", "2026-12-30")).toBe(-3);
  });

  it("numbers days as the API's mask does: Sunday 0", () => {
    expect(dayOfWeek("2026-11-22")).toBe(0);
    expect(dayOfWeek("2026-11-20")).toBe(5);
    expect(maskDays((1 << 1) | (1 << 5))).toEqual([1, 5]);
  });

  it("knows a real date from a malformed one", () => {
    expect(isIsoDate("2026-11-20")).toBe(true);
    expect(isIsoDate("2026-02-30")).toBe(false);
    expect(isIsoDate("2026-11-2")).toBe(false);
    expect(isIsoDate(20261120)).toBe(false);
  });
});

describe("monthGrid", () => {
  it("is whole weeks, Sunday to Saturday, around the month", () => {
    // November 2026 begins on a Sunday and ends on a Monday.
    const weeks = monthGrid("2026-11");

    expect(weeks).toHaveLength(5);
    expect(weeks.every((week) => week.length === 7)).toBe(true);
    expect(weeks[0][0]).toEqual({ date: "2026-11-01", inMonth: true });
    expect(weeks[4][1]).toEqual({ date: "2026-11-30", inMonth: true });
    expect(weeks[4][6]).toEqual({ date: "2026-12-05", inMonth: false });
  });

  it("starts the first week in the month before", () => {
    // October 2026 begins on a Thursday.
    const first = monthGrid("2026-10")[0];
    expect(first[0]).toEqual({ date: "2026-09-27", inMonth: false });
    expect(first[4]).toEqual({ date: "2026-10-01", inMonth: true });
  });
});

describe("monthWindow", () => {
  it("runs from this month to the month of the last bookable day", () => {
    expect(monthWindow("2026-10-25", 90)).toEqual({ first: "2026-10", last: "2027-01" });
    expect(monthWindow("2026-10-25", 6)).toEqual({ first: "2026-10", last: "2026-10" });
    expect(monthWindow("2026-10-25", 7)).toEqual({ first: "2026-10", last: "2026-11" });
  });
});
