/*  Calendar arithmetic on "yyyy-MM-dd" strings. Every date here is a Colombo wall-clock date, the
    same the API speaks, so nothing is ever converted through the browser's own time zone: a donor
    abroad still sees the temple's days. Weeks start on Sunday, as Sri Lankan calendars do and as
    the API's day mask counts (Sunday 1 ... Saturday 64).                                        */

export const COLOMBO = "Asia/Colombo";

/** Today's date in Colombo. */
export function colomboToday(now: Date = new Date()): string {
  // en-CA formats as yyyy-MM-dd.
  return new Intl.DateTimeFormat("en-CA", { timeZone: COLOMBO, year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
}

/** A date's parts; months are 1-12. */
export function parts(iso: string): { year: number; month: number; day: number } {
  const [year, month, day] = iso.split("-").map(Number);
  return { year, month, day };
}

export function iso(year: number, month: number, day: number): string {
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.toISOString().slice(0, 10);
}

export function addDays(date: string, days: number): string {
  const { year, month, day } = parts(date);
  return iso(year, month, day + days);
}

/** "yyyy-MM" of a date. */
export function monthOf(date: string): string {
  return date.slice(0, 7);
}

export function addMonths(month: string, months: number): string {
  const [year, m] = month.split("-").map(Number);
  return iso(year, m + months, 1).slice(0, 7);
}

export function firstOfMonth(month: string): string {
  return `${month}-01`;
}

export function lastOfMonth(month: string): string {
  const [year, m] = month.split("-").map(Number);
  return iso(year, m + 1, 0);
}

/** 0 for Sunday ... 6 for Saturday. */
export function dayOfWeek(date: string): number {
  const { year, month, day } = parts(date);
  return new Date(Date.UTC(year, month - 1, day)).getUTCDay();
}

/** Whether a slot with `mask` runs on `date` (bit 1 << dayOfWeek). */
export function runsOn(mask: number, date: string): boolean {
  return (mask & (1 << dayOfWeek(date))) !== 0;
}

/** A date on weekday `day` (0 Sunday ... 6 Saturday), for Intl to name that weekday. */
export function weekdayDate(day: number): string {
  // 2026-11-01 is a Sunday.
  return addDays("2026-11-01", day);
}

/** The days a mask names, Sunday first. */
export function maskDays(mask: number): number[] {
  return [0, 1, 2, 3, 4, 5, 6].filter((day) => (mask & (1 << day)) !== 0);
}

export function toggleDay(mask: number, day: number): number {
  return mask ^ (1 << day);
}

/**
 * The month as whole weeks, Sunday to Saturday: every date in the grid, with the days of the
 * neighbouring months that fill its first and last week.
 */
export function monthGrid(month: string): { date: string; inMonth: boolean }[][] {
  const first = firstOfMonth(month);
  const last = lastOfMonth(month);
  const start = addDays(first, -dayOfWeek(first));
  const end = addDays(last, 6 - dayOfWeek(last));

  const weeks: { date: string; inMonth: boolean }[][] = [];
  for (let date = start; date <= end; date = addDays(date, 1)) {
    if (dayOfWeek(date) === 0) weeks.push([]);
    weeks[weeks.length - 1].push({ date, inMonth: monthOf(date) === month });
  }
  return weeks;
}

/** Whole days from `from` to `to` (negative when `to` is earlier). */
export function daysBetween(from: string, to: string): number {
  const a = parts(from);
  const b = parts(to);
  return Math.round((Date.UTC(b.year, b.month - 1, b.day) - Date.UTC(a.year, a.month - 1, a.day)) / 86_400_000);
}

/** A date at noon UTC, for Intl to name it without the browser's zone moving it across midnight. */
export function asDate(date: string): Date {
  const { year, month, day } = parts(date);
  return new Date(Date.UTC(year, month - 1, day, 12));
}
