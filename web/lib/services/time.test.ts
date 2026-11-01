import { describe, expect, it } from "vitest";
import { colomboParts, endTime, hoursAndMinutes, requestWindow, startTimes } from "./time";

describe("startTimes", () => {
  it("offers every quarter hour from 05:00 to 21:00 (V11)", () => {
    const times = startTimes();
    expect(times[0]).toBe("05:00");
    expect(times[1]).toBe("05:15");
    expect(times.at(-1)).toBe("21:00");
    expect(times).toHaveLength(65);
    expect(times.every((t) => /^\d{2}:(00|15|30|45)$/.test(t))).toBe(true);
  });
});

describe("colomboParts", () => {
  it("reads a UTC instant as the Colombo date and time", () => {
    expect(colomboParts("2026-11-20T04:30:00Z")).toEqual({ date: "2026-11-20", time: "10:00" });
  });

  it("puts 23:30 UTC on the next Colombo day", () => {
    expect(colomboParts("2026-11-19T23:30:00Z")).toEqual({ date: "2026-11-20", time: "05:00" });
  });

  it("shows midnight as 00:00, not 24:00", () => {
    expect(colomboParts("2026-11-19T18:30:00Z")).toEqual({ date: "2026-11-20", time: "00:00" });
  });
});

describe("endTime", () => {
  it("adds the duration", () => {
    expect(endTime("10:00", 180)).toBe("13:00");
    expect(endTime("21:00", 45)).toBe("21:45");
  });

  it("wraps past midnight", () => {
    expect(endTime("21:00", 240)).toBe("01:00");
  });
});

describe("requestWindow", () => {
  it("counts the advance days from today", () => {
    expect(requestWindow("2026-11-01", 1, 30)).toEqual({ first: "2026-11-02", last: "2026-12-01" });
    expect(requestWindow("2026-11-01", 0, 0)).toEqual({ first: "2026-11-01", last: "2026-11-01" });
  });
});

describe("hoursAndMinutes", () => {
  it("splits a duration", () => {
    expect(hoursAndMinutes(180)).toEqual({ hours: 3, minutes: 0 });
    expect(hoursAndMinutes(90)).toEqual({ hours: 1, minutes: 30 });
    expect(hoursAndMinutes(45)).toEqual({ hours: 0, minutes: 45 });
  });
});
