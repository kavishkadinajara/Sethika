"use client";

import { useFormatter, useLocale, useTranslations, type DateTimeFormatOptions } from "next-intl";
import { useMemo } from "react";
import { asDate, parts } from "@/lib/bookings/calendar";

/*  Dates in words, in the page's language. Chromium ships without Sinhala in its Intl data
    (Intl.DateTimeFormat.supportedLocalesOf(["si"]) is empty), so a Sinhala page formatted through
    Intl reads "October 2026". For Sinhala the names come from the messages instead (CLDR's), in
    CLDR's Sinhala order; English and Tamil use Intl. The choice is by language, never by what the
    browser supports, so the server and the browser always agree.                               */

type Names = { months: string[]; monthsShort: string[]; weekdays: string[]; weekdaysShort: string[]; weekdaysNarrow: string[] };

export function useDates() {
  const locale = useLocale();
  const format = useFormatter();
  const t = useTranslations("Booking.dates");

  return useMemo(() => {
    const own: Names | null =
      locale === "si"
        ? {
            months: t.raw("months") as string[],
            monthsShort: t.raw("monthsShort") as string[],
            weekdays: t.raw("weekdays") as string[],
            weekdaysShort: t.raw("weekdaysShort") as string[],
            weekdaysNarrow: t.raw("weekdaysNarrow") as string[],
          }
        : null;

    const intl = (date: string, options: DateTimeFormatOptions) =>
      format.dateTime(asDate(date), { ...options, timeZone: "UTC" });
    const weekdayOf = (date: string) => asDate(date).getUTCDay();

    const dates = {
      /** "October 2026" */
      monthYear(date: string): string {
        if (!own) return intl(date, { month: "long", year: "numeric" });
        const { year, month } = parts(date);
        return `${year} ${own.months[month - 1]}`;
      },
      /** The weekday's name: "S", "Sun" or "Sunday". */
      weekday(date: string, width: "narrow" | "short" | "long"): string {
        if (!own) return intl(date, { weekday: width });
        const names = width === "narrow" ? own.weekdaysNarrow : width === "short" ? own.weekdaysShort : own.weekdays;
        return names[weekdayOf(date)];
      },
      /** "Oct" */
      monthShort(date: string): string {
        return own ? own.monthsShort[parts(date).month - 1] : intl(date, { month: "short" });
      },
      /** "Sunday, 25 October 2026" */
      full(date: string): string {
        if (!own) return intl(date, { dateStyle: "full" });
        const { year, month, day } = parts(date);
        return `${year} ${own.months[month - 1]} ${day}, ${own.weekdays[weekdayOf(date)]}`;
      },
      /** "25 Oct 2026" */
      medium(date: string): string {
        if (!own) return intl(date, { dateStyle: "medium" });
        const { year, month, day } = parts(date);
        return `${year} ${own.monthsShort[month - 1]} ${day}`;
      },
      /** "Sun, 25 Oct" */
      dayMonth(date: string): string {
        if (!own) return intl(date, { weekday: "short", day: "numeric", month: "short" });
        const { month, day } = parts(date);
        return `${own.weekdaysShort[weekdayOf(date)]}, ${own.monthsShort[month - 1]} ${day}`;
      },
      /** "25 October": the Day Ledger's large date (plan 05, R4). */
      dayMonthLong(date: string): string {
        if (!own) return intl(date, { day: "numeric", month: "long" });
        const { month, day } = parts(date);
        return `${own.months[month - 1]} ${day}`;
      },
      /** "25–27 Oct 2026", or one date when they are the same day. */
      range(from: string, to: string): string {
        if (from === to) return dates.medium(from);
        if (!own) return format.dateTimeRange(asDate(from), asDate(to), { dateStyle: "medium", timeZone: "UTC" });
        return `${dates.medium(from)} – ${dates.medium(to)}`;
      },
    };
    return dates;
  }, [locale, format, t]);
}
