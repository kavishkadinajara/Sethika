import { createTranslator } from "@/i18n/translate";
import { day, mealSlot } from "@/test-utils/fixtures";

import { canChoose, dayState, dayWords, headcountLimits, readHeadcount, slotDays, stepHeadcount } from "./availability";

/*  The API's judgement of a day (DayAvailabilityView) turned into the bowl calendar's picture
    and words. The API decides; these only show it, and never by colour alone.               */

const shared = mealSlot({ bookingMode: "Shared", maxSponsors: 4 });
const exclusive = mealSlot({ bookingMode: "Exclusive", maxSponsors: 1 });
const t = createTranslator("en");
const words = (state: ReturnType<typeof dayState>) => {
  const said = dayWords(state);
  return said ? t(said.key, said.values) : null;
};

describe("dayState", () => {
  it("shows a shared day nobody has booked as an empty bowl", () => {
    const state = dayState(shared, day("2026-11-20", { sponsors: 4 }));
    expect(state).toEqual({ kind: "open", fill: 0, placesLeft: 4 });
    expect(words(state)).toBe("4 places left");
  });

  it("fills the bowl by the share of places taken", () => {
    const state = dayState(shared, day("2026-11-20", { sponsors: 1 }));
    expect(state).toEqual({ kind: "filling", fill: 0.75, placesLeft: 1 });
    expect(words(state)).toBe("1 place left");
  });

  it("shows an open exclusive day as an empty bowl that says Open", () => {
    const state = dayState(exclusive, day("2026-11-20", { sponsors: 1 }));
    expect(state).toEqual({ kind: "open", fill: 0, placesLeft: null });
    expect(words(state)).toBe("Open");
  });

  it("shows a full day as a full bowl, in words too", () => {
    const state = dayState(shared, day("2026-11-20", { bookable: false, reason: "slot_full", sponsors: 0 }));
    expect(state).toEqual({ kind: "full", fill: 1 });
    expect(words(state)).toBe("Full");
  });

  it("shows a day that cannot be booked for another reason as a plain date with its reason", () => {
    expect(words(dayState(shared, day("2026-11-20", { bookable: false, reason: "date_blocked" })))).toBe("Closed this day");
    expect(words(dayState(shared, day("2026-11-20", { bookable: false, reason: "too_soon" })))).toBe("Too soon to book");
    expect(words(dayState(shared, day("2026-11-20", { bookable: false, reason: "not_on_this_day" })))).toBe("Not served this day");
    expect(dayState(shared, day("2026-11-20", { bookable: false, reason: "too_far" }))).toEqual({ kind: "closed", reason: "too_far" });
  });

  it("says nothing about a day it has not been told about", () => {
    expect(dayState(shared, undefined)).toEqual({ kind: "unknown" });
    expect(dayWords({ kind: "unknown" })).toBeNull();
  });

  it("lets only open and filling days open the booking sheet", () => {
    expect(canChoose({ kind: "open", fill: 0, placesLeft: 4 })).toBe(true);
    expect(canChoose({ kind: "filling", fill: 0.5, placesLeft: 2 })).toBe(true);
    expect(canChoose({ kind: "full", fill: 1 })).toBe(false);
    expect(canChoose({ kind: "closed", reason: "date_blocked" })).toBe(false);
    expect(canChoose({ kind: "unknown" })).toBe(false);
  });
});

describe("slotDays", () => {
  it("finds the chosen slot's days by date, and none while the month loads", () => {
    const days = slotDays(
      { slots: [{ mealSlot: "other", days: [day("2026-11-01")] }, { mealSlot: shared.publicId, days: [day("2026-11-02")] }] },
      shared.publicId,
    );
    expect([...days!.keys()]).toEqual(["2026-11-02"]);
    expect(slotDays(undefined, shared.publicId)).toBeUndefined();
  });
});

describe("headcount", () => {
  it("allows the slot's range, capped by what the day has left", () => {
    expect(headcountLimits(shared, day("2026-11-20", { headcount: 60 }))).toEqual({ min: 10, max: 60 });
    expect(headcountLimits(shared, day("2026-11-20", { headcount: 25 }))).toEqual({ min: 10, max: 25 });
    // A day with fewer people left than the minimum is the API's to refuse; the range never inverts.
    expect(headcountLimits(shared, day("2026-11-20", { headcount: 5 }))).toEqual({ min: 10, max: 10 });
  });

  it("reads only whole numbers in range", () => {
    const limits = { min: 10, max: 25 };
    expect(readHeadcount(" 25 ", limits)).toBe(25);
    expect(readHeadcount("26", limits)).toBeNull();
    expect(readHeadcount("9", limits)).toBeNull();
    expect(readHeadcount("1O", limits)).toBeNull();
    expect(readHeadcount("12.5", limits)).toBeNull();
  });

  it("steps within range, and from the minimum when the field is not a number", () => {
    const limits = { min: 10, max: 25 };
    expect(stepHeadcount("24", 1, limits)).toBe("25");
    expect(stepHeadcount("25", 1, limits)).toBe("25");
    expect(stepHeadcount("10", -1, limits)).toBe("10");
    expect(stepHeadcount("", 1, limits)).toBe("11");
  });
});
