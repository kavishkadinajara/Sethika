import { COLOMBO } from "@/lib/bookings/calendar";

export type PartOfDay = "morning" | "afternoon" | "evening";

/**
 * The dashboard's greeting by the hour in Colombo, not the browser's zone: Sethika's day is Sri
 * Lanka's (a donor abroad booking tomorrow's lunch means Colombo's tomorrow). Morning from 04:00,
 * afternoon from 12:00, evening from 17:00 until the next morning.
 */
export function partOfDay(now: Date = new Date()): PartOfDay {
  const hour = Number(
    new Intl.DateTimeFormat("en-GB", { timeZone: COLOMBO, hour: "numeric", hourCycle: "h23" }).format(now),
  );
  if (hour >= 4 && hour < 12) return "morning";
  if (hour >= 12 && hour < 17) return "afternoon";
  return "evening";
}
