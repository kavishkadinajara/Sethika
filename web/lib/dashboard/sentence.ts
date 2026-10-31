import { COLOMBO, addDays } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import type { DonorBooking, TodaysTable } from "@/lib/bookings/types";

/*  The Day Ledger's one sentence (plan 05, R4): what today needs, composed from data, never
    decoration. Each composer picks a message under `Dashboard.sentence` and the values it needs;
    the screen formats dates and plurals through next-intl. Pure, so every case is unit-tested.  */

export type Sentence = { key: string; values: Record<string, string | number> };

/** "HH:mm" on Colombo's clock. */
export function colomboTime(now: Date = new Date()): string {
  return new Intl.DateTimeFormat("en-GB", { timeZone: COLOMBO, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(now);
}

/**
 * For institution staff. `table` is null when the session may not read the calendar, `waiting`
 * null when it may not answer requests: a number it may not see is never in the sentence.
 */
export function institutionSentence({
  table,
  waiting,
  locale,
  now,
}: {
  table: TodaysTable | null;
  waiting: number | null;
  locale: string;
  now: string;
}): Sentence {
  const laid = table?.slots.filter((s) => s.state !== "Closed") ?? [];
  const next = laid.find((s) => s.startTime > now);
  const meal = next ? { meal: inLocale(locale, next.name, next.nameSi, next.nameTa), time: next.startTime } : null;

  if (waiting) {
    return meal ? { key: "waitingAndNext", values: { count: waiting, ...meal } } : { key: "waiting", values: { count: waiting } };
  }

  if (table === null) return { key: "institutionPlain", values: {} };
  if (laid.length === 0) return { key: "noMeals", values: {} };
  if (!meal || !next) return { key: "mealsOver", values: {} };

  // Places still open on the meals yet to come; one already begun can no longer be given.
  const open = laid
    .filter((s) => s.startTime > now && s.state === "Open")
    .reduce((sum, s) => sum + (s.maxSponsors - s.bookedSponsors), 0);

  return open > 0 ? { key: "nextWithOpen", values: { ...meal, open } } : { key: "nextAllGiven", values: meal };
}

/**
 * For a donor: the next meal they give. A meal still waiting for the institution's answer says
 * so; one already begun today is not "next".
 */
export function donorSentence({
  bookings,
  locale,
  today,
  now,
}: {
  bookings: DonorBooking[] | null;
  locale: string;
  today: string;
  now: string;
}): Sentence {
  if (bookings === null) return { key: "donorPlain", values: {} };

  const next = upcoming(bookings, today, now)[0];
  if (!next) return { key: "nothingBooked", values: {} };

  const values = {
    meal: inLocale(locale, next.slotName, next.slotNameSi, next.slotNameTa),
    place: inLocale(locale, next.institutionName, next.institutionNameSi, next.institutionNameTa),
    time: next.startTime,
    date: next.date,
  };
  const when = next.date === today ? "Today" : next.date === addDays(today, 1) ? "Tomorrow" : "Later";
  return { key: `${next.status === "Requested" ? "asked" : "giving"}${when}`, values };
}

/** A donor's meals still to give, soonest first: requested or approved, not yet begun. */
export function upcoming(bookings: DonorBooking[], today: string, now: string): DonorBooking[] {
  return bookings
    .filter((b) => (b.status === "Requested" || b.status === "Approved") && (b.date > today || (b.date === today && b.startTime > now)))
    .sort((a, b) => (a.date === b.date ? a.startTime.localeCompare(b.startTime) : a.date.localeCompare(b.date)));
}

/** For the platform: the review queue's length and how long its oldest has waited. */
export function platformSentence({
  waiting,
  oldestDays,
}: {
  waiting: number | null;
  oldestDays: number | null;
}): Sentence {
  if (waiting === null) return { key: "platformPlain", values: {} };
  if (waiting === 0 || oldestDays === null) return { key: "queueClear", values: {} };
  return { key: "queue", values: { count: waiting, days: oldestDays } };
}
