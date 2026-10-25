import { createTranslator, listOf, type Language, type Translate } from "@/i18n/translate";

import { dayOfWeek, parts } from "./calendar";

/*  Dates in words, in the screen's language, with every month and weekday name taken from the
    messages (Booking.dates, CLDR's names, copied from the web) and every order from App.dates.

    Not Intl, in any language. Hermes implements Intl on top of the phone's own ICU (Android) or
    Foundation (iOS), so what a phone says depends on its OS version, not on this app: CLDR 48
    (Node 22's ICU 78) already names the Sinhala months දුරුතු ... උඳුවප්, where the web and
    older phones say ජනවාරි ... දෙසැම්බර්. The web keeps Intl for English and Tamil because a
    browser's Intl is its own; a phone's is not, so here all three come from the messages and
    every phone shows the same words.                                                         */

export type Dates = {
  /** "November 2026" */
  monthYear(date: string): string;
  /** The weekday's name: "S", "Sun" or "Sunday". */
  weekday(date: string, width: "narrow" | "short" | "long"): string;
  /** A weekday by number (0 Sunday ... 6 Saturday), for a slot's days. */
  weekdayName(day: number, width: "narrow" | "short" | "long"): string;
  /** "Nov" */
  monthShort(date: string): string;
  /** "Sunday, 1 November 2026" */
  full(date: string): string;
  /** "Sun, 1 Nov" */
  dayMonth(date: string): string;
};

export function createDates(language: Language, t: Translate = createTranslator(language)): Dates {
  const months = listOf(language, "Booking.dates.months");
  const monthsShort = listOf(language, "Booking.dates.monthsShort");
  const weekdays = {
    long: listOf(language, "Booking.dates.weekdays"),
    short: listOf(language, "Booking.dates.weekdaysShort"),
    narrow: listOf(language, "Booking.dates.weekdaysNarrow"),
  };

  // Strings, not numbers: ICU would format a number argument, and 2026 would read "2,026".
  const values = (date: string, names: readonly string[], weekdayNames: readonly string[]) => {
    const { year, month, day } = parts(date);
    return { year: String(year), month: names[month - 1], day: String(day), weekday: weekdayNames[dayOfWeek(date)] };
  };

  return {
    monthYear: (date) => t("App.dates.monthYear", values(date, months, weekdays.long)),
    weekday: (date, width) => weekdays[width][dayOfWeek(date)],
    weekdayName: (day, width) => weekdays[width][day],
    monthShort: (date) => monthsShort[parts(date).month - 1],
    full: (date) => t("App.dates.full", values(date, months, weekdays.long)),
    dayMonth: (date) => t("App.dates.dayMonth", values(date, monthsShort, weekdays.short)),
  };
}
