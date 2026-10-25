import { describe, expect, it } from "vitest";
import {
  addDays,
  addMonths,
  colomboToday,
  dayOfWeek,
  daysBetween,
  lastOfMonth,
  maskDays,
  monthGrid,
  runsOn,
  toggleDay,
} from "./calendar";

describe("colomboToday", () => {
  it("is Colombo's date, not UTC's: 20:00 UTC is already tomorrow there", () => {
    expect(colomboToday(new Date("2026-11-19T20:00:00Z"))).toBe("2026-11-20");
    expect(colomboToday(new Date("2026-11-19T18:29:00Z"))).toBe("2026-11-19");
  });
});

describe("date arithmetic", () => {
  it("crosses month and year ends", () => {
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
  });
});

describe("the day mask", () => {
  const mondaysAndFridays = (1 << 1) | (1 << 5);

  it("says which days a slot runs on", () => {
    expect(runsOn(mondaysAndFridays, "2026-11-20")).toBe(true);
    expect(runsOn(mondaysAndFridays, "2026-11-21")).toBe(false);
    expect(runsOn(127, "2026-11-21")).toBe(true);
  });

  it("lists and toggles its days", () => {
    expect(maskDays(mondaysAndFridays)).toEqual([1, 5]);
    expect(toggleDay(mondaysAndFridays, 5)).toBe(1 << 1);
    expect(toggleDay(0, 0)).toBe(1);
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
