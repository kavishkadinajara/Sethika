/*  Calendar arithmetic on "yyyy-MM-dd" strings, the twin of web/lib/bookings/calendar.ts. Every
    date is a Colombo wall-clock date, the same the API speaks, so nothing passes through the
    phone's own time zone: a donor abroad still sees the temple's days. Weeks start on Sunday, as
    Sri Lankan calendars do and as the API's day mask counts (Sunday 1 ... Saturday 64).

    Colombo is UTC+05:30 all year (no daylight saving since 2006), so today's date there is plain
    arithmetic and needs no Intl time-zone data, which Hermes takes from the phone.              */

const COLOMBO_OFFSET_MS = (5 * 60 + 30) * 60 * 1000;

/** Today's date in Colombo. */
export function colomboToday(now: Date = new Date()): string {
  return new Date(now.getTime() + COLOMBO_OFFSET_MS).toISOString().slice(0, 10);
}

/** The part of the day in Colombo, for a greeting: before noon, before six, or later. */
export function colomboPartOfDay(now: Date = new Date()): "morning" | "afternoon" | "evening" {
  const hour = new Date(now.getTime() + COLOMBO_OFFSET_MS).getUTCHours();
  return hour < 12 ? "morning" : hour < 18 ? "afternoon" : "evening";
}

/** A date's parts; months are 1-12. */
export function parts(date: string): { year: number; month: number; day: number } {
  const [year, month, day] = date.split("-").map(Number);
  return { year, month, day };
}

export function iso(year: number, month: number, day: number): string {
  return new Date(Date.UTC(year, month - 1, day)).toISOString().slice(0, 10);
}

export function isIsoDate(value: unknown): value is string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const { year, month, day } = parts(value);
  return iso(year, month, day) === value;
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

/** The days a mask names, Sunday first. */
export function maskDays(mask: number): number[] {
  return [0, 1, 2, 3, 4, 5, 6].filter((day) => (mask & (1 << day)) !== 0);
}

/** Whole days from `from` to `to` (negative when `to` is earlier). */
export function daysBetween(from: string, to: string): number {
  const a = parts(from);
  const b = parts(to);
  return Math.round((Date.UTC(b.year, b.month - 1, b.day) - Date.UTC(a.year, a.month - 1, a.day)) / 86_400_000);
}

/** One cell of the month grid; the neighbouring months' days have `inMonth` false and show blank. */
export type GridCell = { date: string; inMonth: boolean };

/**
 * The month as whole weeks, Sunday to Saturday: every date in the grid, with the days of the
 * neighbouring months that fill its first and last week.
 */
export function monthGrid(month: string): GridCell[][] {
  const first = firstOfMonth(month);
  const last = lastOfMonth(month);
  const start = addDays(first, -dayOfWeek(first));
  const end = addDays(last, 6 - dayOfWeek(last));

  const weeks: GridCell[][] = [];
  for (let date = start; date <= end; date = addDays(date, 1)) {
    if (dayOfWeek(date) === 0) weeks.push([]);
    weeks[weeks.length - 1].push({ date, inMonth: monthOf(date) === month });
  }
  return weeks;
}

/**
 * The months a donor may page between for a slot: from this month to the one holding the last
 * day the slot can be booked (MaxAdvanceDays ahead). The API judges every day again; this only
 * stops the arrows at months that cannot hold an open day.
 */
export function monthWindow(today: string, maxAdvanceDays: number): { first: string; last: string } {
  return { first: monthOf(today), last: monthOf(addDays(today, Math.max(0, maxAdvanceDays))) };
}
