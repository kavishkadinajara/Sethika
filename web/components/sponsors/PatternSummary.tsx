"use client";

import { useTranslations } from "next-intl";
import { useDates } from "@/components/bookings/useDates";
import { weekdayDate } from "@/lib/bookings/calendar";
import type { Sponsorship } from "@/lib/sponsors/types";

/** The leap year a yearly day is named in, so 29 February has a date to format. */
const LEAP_YEAR = 2000;

/**
 * A sponsorship's pattern in a sentence: "Every other month, on days 5, 20 · from 5 Nov 2026". The
 * weekday and month names come from the messages (Chromium has no Sinhala dates, useDates).
 */
export function usePatternSummary() {
  const t = useTranslations("Sponsors.pattern");
  const dates = useDates();

  return (s: Pick<Sponsorship, "pattern" | "monthInterval" | "weekDays" | "monthDays" | "yearMonth" | "yearDay" | "startDate" | "endDate">) => {
    const pattern = (() => {
      switch (s.pattern) {
        case "Once":
          return t("Once", { date: dates.medium(s.startDate) });
        case "Daily":
          return t("Daily");
        case "Weekly":
          return t("Weekly", { days: s.weekDays.map((d) => dates.weekday(weekdayDate(d), "long")).join(", ") });
        case "Monthly":
          return t("Monthly", { interval: s.monthInterval, count: s.monthDays.length, days: s.monthDays.join(", ") });
        case "Yearly":
          return t("Yearly", {
            date: dates.dayMonthLong(`${LEAP_YEAR}-${String(s.yearMonth).padStart(2, "0")}-${String(s.yearDay).padStart(2, "0")}`),
          });
      }
    })();

    if (s.pattern === "Once") return pattern;
    const range = [t("from", { date: dates.medium(s.startDate) }), s.endDate ? t("until", { date: dates.medium(s.endDate) }) : null].filter(Boolean).join(" ");
    return `${pattern} · ${range}`;
  };
}
