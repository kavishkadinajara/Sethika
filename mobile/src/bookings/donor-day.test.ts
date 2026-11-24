import { ANONYMOUS, DONOR, STAFF } from "@/test-utils/api-stub";
import { booking } from "@/test-utils/fixtures";
import { LANGUAGES } from "@/i18n/translate";

import { donorSentence, mayReadOwnBookings, mealsToGive, OWN_BOOKINGS_VIEW, TABLE_SHOWN } from "./donor-day";

/*  The same cases as web/lib/dashboard/sentence.test.ts: the two ports must say alike. Today is
    2026-11-10 and it is 10:00 in Colombo.                                                     */

// The app has no Node types, as in colors.test.ts.
declare const __dirname: string;
const { readFileSync } = jest.requireActual<{ readFileSync: (path: string, encoding: "utf8") => string }>("fs");

const TODAY = "2026-11-10";
const NOW = "10:00";
const sentence = (bookings: Parameters<typeof donorSentence>[0]["bookings"], language: "en" | "si" | "ta" = "en") =>
  donorSentence({ bookings, language, today: TODAY, now: NOW });

describe("donorSentence", () => {
  it("invites when the bookings could not be read, and never says nothing is booked", () => {
    expect(sentence(null).key).toBe("Dashboard.sentence.donorPlain");
  });

  it("says nothing is booked only after reading that", () => {
    expect(sentence([]).key).toBe("Dashboard.sentence.nothingBooked");
  });

  it.each([
    ["Approved", TODAY, "givingToday"],
    ["Approved", "2026-11-11", "givingTomorrow"],
    ["Approved", "2026-11-12", "givingLater"],
    ["Requested", TODAY, "askedToday"],
    ["Requested", "2026-11-11", "askedTomorrow"],
    ["Requested", "2026-11-12", "askedLater"],
  ] as const)("a %s meal on %s reads %s", (status, date, key) => {
    expect(sentence([booking({ status, date, startTime: "11:30" })]).key).toBe(`Dashboard.sentence.${key}`);
  });

  it("names the meal, the place, the time and the date in the donor's language", () => {
    const next = booking({ date: "2026-11-12", slotNameSi: "දිවා ආහාරය", institutionNameSi: "නිවාසය" });
    expect(sentence([next], "si").values).toEqual({ meal: "දිවා ආහාරය", place: "නිවාසය", time: "11:30", date: "2026-11-12" });
  });

  it("speaks of the soonest meal", () => {
    const later = booking({ bookingNo: "BK-2", date: "2026-11-13", slotName: "Later" });
    const sooner = booking({ bookingNo: "BK-1", date: "2026-11-12", slotName: "Sooner" });
    expect(sentence([later, sooner]).values.meal).toBe("Sooner");
  });

  it("passes over a meal already begun today", () => {
    const begun = booking({ bookingNo: "BK-1", date: TODAY, startTime: "09:00", slotName: "Breakfast" });
    const next = booking({ bookingNo: "BK-2", date: "2026-11-11", slotName: "Lunch" });
    expect(sentence([begun, next])).toMatchObject({ key: "Dashboard.sentence.givingTomorrow", values: { meal: "Lunch" } });
  });
});

describe("mealsToGive", () => {
  const no = (list: readonly { bookingNo: string }[]) => list.map((item) => item.bookingNo);

  it("keeps requested and approved meals, and no other", () => {
    const list = ["Requested", "Approved", "Rejected", "Cancelled", "Completed", "NoShow"].map((status) =>
      booking({ bookingNo: status, status: status as never, date: "2026-11-12" }),
    );
    expect(no(mealsToGive(list, TODAY, NOW))).toEqual(["Requested", "Approved"]);
  });

  it("drops the past, and a meal today at or before this minute", () => {
    const list = [
      booking({ bookingNo: "yesterday", date: "2026-11-09", startTime: "23:00" }),
      booking({ bookingNo: "begun", date: TODAY, startTime: "09:59" }),
      booking({ bookingNo: "starting", date: TODAY, startTime: "10:00" }),
      booking({ bookingNo: "soon", date: TODAY, startTime: "10:01" }),
    ];
    expect(no(mealsToGive(list, TODAY, NOW))).toEqual(["soon"]);
  });

  it("puts two meals on one day in time order, and leaves the list it was given alone", () => {
    const list = [
      booking({ bookingNo: "dinner", date: "2026-11-12", startTime: "18:00" }),
      booking({ bookingNo: "next-day", date: "2026-11-13", startTime: "07:00" }),
      booking({ bookingNo: "lunch", date: "2026-11-12", startTime: "11:30" }),
    ];
    expect(no(mealsToGive(list, TODAY, NOW))).toEqual(["lunch", "dinner", "next-day"]);
    expect(no(list)).toEqual(["dinner", "next-day", "lunch"]);
  });
});

describe("mayReadOwnBookings", () => {
  const holds = (code: string) => code === OWN_BOOKINGS_VIEW;

  it("is for someone acting as themselves who holds the permission", () => {
    expect(OWN_BOOKINGS_VIEW).toBe("MYBOOKINGS.BOOKINGS.VIEW");
    expect(mayReadOwnBookings(DONOR, holds)).toBe(true);
    expect(mayReadOwnBookings(DONOR, () => false)).toBe(false);
  });

  it("is not for a visitor or a session acting for an institution", () => {
    expect(mayReadOwnBookings(ANONYMOUS, holds)).toBe(false);
    expect(mayReadOwnBookings(STAFF, holds)).toBe(false);
  });
});

it("lists three meals on a phone (F22)", () => {
  expect(TABLE_SHOWN).toBe(3);
});

describe("the words", () => {
  const read = (path: string) => JSON.parse(readFileSync(`${__dirname}/${path}`, "utf8")) as { Dashboard: Record<string, Record<string, string>> };

  it.each(LANGUAGES)("in %s are the web's own", (language) => {
    const app = read(`../i18n/messages/${language}.json`).Dashboard;
    const web = read(`../../../web/messages/${language}.json`).Dashboard;

    for (const key of ["donorPlain", "nothingBooked", "givingToday", "givingTomorrow", "givingLater", "askedToday", "askedTomorrow", "askedLater"]) {
      expect(app.sentence[key]).toBe(web.sentence[key]);
    }
    expect(app.panels.yourTable).toBe(web.panels.yourTable);
    expect(app.panels.allBookings).toBe(web.panels.allBookings);
    expect(app.welcome).toBe(web.welcome);
  });
});
