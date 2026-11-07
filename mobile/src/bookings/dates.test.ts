import { createDates } from "./dates";

/*  Dates in words come from the messages in every language, never from the phone's Intl
    (dates.ts says why). 2026-11-01 is a Sunday.                                           */

describe("createDates", () => {
  it("speaks English", () => {
    const dates = createDates("en");
    expect(dates.monthYear("2026-11-01")).toBe("November 2026");
    expect(dates.full("2026-11-01")).toBe("Sunday, 1 November 2026");
    expect(dates.dayMonth("2026-11-20")).toBe("Fri, 20 Nov");
    expect(dates.dayMonthLong("2026-11-20")).toBe("20 November");
    expect(dates.weekday("2026-11-01", "narrow")).toBe("S");
    expect(dates.weekdayName(6, "short")).toBe("Sat");
    expect(dates.monthShort("2026-01-15")).toBe("Jan");
  });

  it("speaks Sinhala with the Gregorian month names, in the Sinhala order", () => {
    const dates = createDates("si");
    expect(dates.monthYear("2026-11-01")).toBe("2026 නොවැම්බර්");
    expect(dates.full("2026-11-01")).toBe("2026 නොවැම්බර් 1, ඉරිදා");
    expect(dates.weekday("2026-11-03", "long")).toBe("අඟහරුවාදා");
    // Not the traditional names a newer ICU gives (ඉල් for November).
    expect(dates.monthYear("2026-11-01")).not.toContain("ඉල්");
  });

  it("speaks Tamil", () => {
    const dates = createDates("ta");
    expect(dates.monthYear("2026-11-01")).toBe("நவம்பர் 2026");
    expect(dates.full("2026-11-01")).toBe("ஞாயிறு, 1 நவம்பர், 2026");
    expect(dates.weekday("2026-11-02", "narrow")).toBe("தி");
  });

  it("never groups the year's digits", () => {
    expect(createDates("en").full("2026-12-31")).toBe("Thursday, 31 December 2026");
  });
});

it("writes the long date as each language does, as the web does", () => {
  expect(createDates("si").dayMonthLong("2026-11-20")).toBe("නොවැම්බර් 20");
  expect(createDates("ta").dayMonthLong("2026-11-20")).toBe("20 நவம்பர்");
});
