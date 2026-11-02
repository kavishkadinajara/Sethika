import { describe, expect, it } from "vitest";
import { byDonor, canSkip, isDayOfSomeYear, patternFields, patternProblems, swapPartners, swappableDays, type PatternForm } from "./pattern";
import type { Sponsorship, SponsorshipDay } from "./types";

const form = (patch: Partial<PatternForm> = {}): PatternForm => ({
  pattern: "Monthly",
  weekDays: [5, 1],
  monthDays: [20, 5],
  monthInterval: 2,
  yearMonth: 2,
  yearDay: 29,
  startDate: "2026-11-05",
  endDate: "",
  ...patch,
});

describe("patternFields", () => {
  it("sends only the chosen pattern's own fields", () => {
    expect(patternFields(form())).toEqual({ pattern: "Monthly", startDate: "2026-11-05", endDate: null, monthDays: [5, 20], monthInterval: 2 });
    expect(patternFields(form({ pattern: "Weekly", endDate: "2027-01-01" }))).toEqual({ pattern: "Weekly", startDate: "2026-11-05", endDate: "2027-01-01", weekDays: [1, 5] });
    expect(patternFields(form({ pattern: "Yearly" }))).toEqual({ pattern: "Yearly", startDate: "2026-11-05", endDate: null, yearMonth: 2, yearDay: 29 });
    expect(patternFields(form({ pattern: "Daily" }))).toEqual({ pattern: "Daily", startDate: "2026-11-05", endDate: null });
  });

  it("gives a single day no end of its own", () => {
    expect(patternFields(form({ pattern: "Once", endDate: "2027-01-01" }))).toEqual({ pattern: "Once", startDate: "2026-11-05" });
  });
});

describe("isDayOfSomeYear", () => {
  it("takes 29 February and the 31st of a long month, never the 31st of a short one", () => {
    expect(isDayOfSomeYear(2, 29)).toBe(true);
    expect(isDayOfSomeYear(2, 30)).toBe(false);
    expect(isDayOfSomeYear(1, 31)).toBe(true);
    expect(isDayOfSomeYear(4, 31)).toBe(false);
    expect(isDayOfSomeYear(4, 30)).toBe(true);
    expect(isDayOfSomeYear(12, 31)).toBe(true);
    expect(isDayOfSomeYear(13, 1)).toBe(false);
    expect(isDayOfSomeYear(1, 0)).toBe(false);
  });
});

describe("patternProblems", () => {
  const today = "2026-11-02";

  it("finds nothing in a whole form", () => {
    expect(patternProblems(form(), today)).toEqual([]);
    expect(patternProblems(form({ startDate: today }), today)).toEqual([]);
  });

  it("needs a weekday for a weekly pattern and a day for a monthly one", () => {
    expect(patternProblems(form({ pattern: "Weekly", weekDays: [] }), today)).toEqual(["weekDays"]);
    expect(patternProblems(form({ monthDays: [] }), today)).toEqual(["monthDays"]);
    // The other pattern's empty choice does not matter.
    expect(patternProblems(form({ pattern: "Daily", weekDays: [], monthDays: [] }), today)).toEqual([]);
  });

  it("refuses a day no year has", () => {
    expect(patternProblems(form({ pattern: "Yearly", yearMonth: 4, yearDay: 31 }), today)).toEqual(["yearDay"]);
  });

  it("starts today or later, and ends on or after the start", () => {
    expect(patternProblems(form({ startDate: "2026-11-01" }), today)).toEqual(["startDate"]);
    expect(patternProblems(form({ startDate: "" }), today)).toEqual(["startDate"]);
    expect(patternProblems(form({ endDate: "2026-11-04" }), today)).toEqual(["endDate"]);
    expect(patternProblems(form({ endDate: "2026-11-05" }), today)).toEqual([]);
    expect(patternProblems(form({ pattern: "Once", endDate: "2026-11-04" }), today)).toEqual([]);
  });
});

const sponsorship = (publicId: string, patch: Partial<Sponsorship> = {}): Sponsorship => ({
  publicId,
  donor: "d1",
  donorName: "Sunil Perera",
  mealSlot: "lunch",
  mealSlotName: "Lunch",
  pattern: "Monthly",
  monthInterval: 1,
  weekDays: [],
  monthDays: [5],
  yearMonth: null,
  yearDay: null,
  startDate: "2026-11-05",
  endDate: null,
  headcount: 25,
  fulfilment: "DonorBrings",
  inMemoryOf: null,
  notes: null,
  status: "Active",
  endedAt: null,
  maxAdvanceDays: 60,
  version: "AAAAAAAAB9E=",
  ...patch,
});

describe("byDonor", () => {
  it("groups each donor's arrangements, the active ones first, by start", () => {
    const groups = byDonor([
      sponsorship("ended", { status: "Ended", startDate: "2026-01-01" }),
      sponsorship("later", { startDate: "2026-12-01" }),
      sponsorship("other", { donor: "d2" }),
      sponsorship("sooner", { startDate: "2026-11-10" }),
    ]);
    expect(groups.get("d1")?.map((s) => s.publicId)).toEqual(["sooner", "later", "ended"]);
    expect(groups.get("d2")?.map((s) => s.publicId)).toEqual(["other"]);
  });
});

const day = (date: string, state: SponsorshipDay["state"], bookingStatus: SponsorshipDay["bookingStatus"] = null): SponsorshipDay => ({
  date,
  state,
  bookingNo: bookingStatus ? "BK-2026-000001" : null,
  bookingStatus,
  reason: null,
});

describe("canSkip", () => {
  const today = "2026-11-05";

  it("lets go a day from today on that is still the sponsor's", () => {
    expect(canSkip(day(today, "booked", "Approved"), today)).toBe(true);
    expect(canSkip(day("2026-12-05", "open"), today)).toBe(true);
    expect(canSkip(day("2027-05-05", "upcoming"), today)).toBe(true);
    expect(canSkip(day("2026-12-05", "cancelled", "Cancelled"), today)).toBe(true);
  });

  it("never a past day, a meal already given, or one already let go or moved", () => {
    expect(canSkip(day("2026-11-04", "booked", "Approved"), today)).toBe(false);
    expect(canSkip(day(today, "booked", "Completed"), today)).toBe(false);
    expect(canSkip(day(today, "booked", "NoShow"), today)).toBe(false);
    for (const state of ["Skipped", "Released", "Blocked", "MovedTo", "MovedFrom"] as const) expect(canSkip(day("2026-12-05", state), today)).toBe(false);
  });
});

describe("swappableDays", () => {
  const today = "2026-11-05";
  const days = [
    day("2026-11-04", "booked", "Approved"),
    day(today, "booked", "Approved"),
    day("2026-11-06", "booked", "Completed"),
    day("2026-12-05", "open"),
    day("2027-01-05", "Skipped"),
    day("2027-02-05", "upcoming"),
    day("2027-03-05", "booked", "Requested"),
  ];

  it("takes booked days this time, and any day still the pattern's from now on", () => {
    expect(swappableDays(days, today, false).map((d) => d.date)).toEqual([today, "2027-03-05"]);
    expect(swappableDays(days, today, true).map((d) => d.date)).toEqual([today, "2026-12-05", "2027-02-05", "2027-03-05"]);
  });
});

describe("swapPartners", () => {
  it("offers the other active arrangements of the same meal", () => {
    const own = sponsorship("own");
    const all = [own, sponsorship("same"), sponsorship("dinner", { mealSlot: "dinner" }), sponsorship("ended", { status: "Ended" })];
    expect(swapPartners(all, own).map((s) => s.publicId)).toEqual(["same"]);
  });
});
