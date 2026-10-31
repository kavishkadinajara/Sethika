import { describe, expect, it } from "vitest";
import { partOfDay } from "./greeting";

// Colombo is UTC+05:30 all year.
const colombo = (time: string) => new Date(`2026-10-25T${time}:00+05:30`);

describe("part of the day in Colombo", () => {
  it.each([
    ["03:59", "evening"],
    ["04:00", "morning"],
    ["11:59", "morning"],
    ["12:00", "afternoon"],
    ["16:59", "afternoon"],
    ["17:00", "evening"],
    ["23:30", "evening"],
  ])("%s is %s", (time, expected) => {
    expect(partOfDay(colombo(time))).toBe(expected);
  });

  it("goes by Colombo's clock, not UTC's", () => {
    // 07:00 UTC is 12:30 in Colombo: afternoon there, still morning in UTC.
    expect(partOfDay(new Date("2026-10-25T07:00:00Z"))).toBe("afternoon");
  });
});
