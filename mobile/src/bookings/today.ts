import type { SessionState } from "@/auth/session-client";
import type { Language, MessageKey } from "@/i18n/translate";

import { bookingErrorKey, type BookingFailure } from "./errors";
import { inLocale } from "./names";
import type { CalendarBooking, SlotDay, TableSlot, TableSlotState, TodaysTable } from "./types";

/*  The Today tab's rules (F23, F24), framework-free so every case is unit-tested. The sentence is a
    port of institutionSentence in web/lib/dashboard/sentence.ts: staff read the same words on both. */

export const CALENDAR_VIEW = "BOOKINGS.CALENDAR.VIEW";
export const CALENDAR_EDIT = "BOOKINGS.CALENDAR.EDIT";
export const CALENDAR_APPROVE = "BOOKINGS.CALENDAR.APPROVE";
export const CALENDAR_REJECT = "BOOKINGS.CALENDAR.REJECT";

/** How far ahead the sentence counts requests: the web's reach, and the API's widest read. */
export const WAITING_DAYS = 62;

/**
 * Whether this session has a Today. The session must act for an institution: the routes answer
 * `no_institution` without one, whatever the permissions, and a platform account holds them too.
 */
export function mayOpenToday(session: SessionState, can: (code: string) => boolean): boolean {
  return session.status === "authenticated" && session.activeInstitution !== null && can(CALENDAR_VIEW);
}

export type TodayMeal = {
  mealSlot: string;
  name: string;
  nameSi: string | null;
  nameTa: string | null;
  startTime: string;
  maxSponsors: number;
  bookedSponsors: number;
  requested: number;
  state: TableSlotState;
  sponsors: CalendarBooking[];
};

/** Whoever still stands behind the meal: a rejected or cancelled booking gave its place back. */
function sponsorsOf(day: SlotDay | undefined): CalendarBooking[] {
  return day?.bookings.filter((booking) => booking.status !== "Rejected" && booking.status !== "Cancelled") ?? [];
}

/**
 * Today's table with each meal's sponsors. The table holds only the meals laid today, so a meal
 * switched off after it was booked is not on it. Its sponsors are still coming, so it joins from
 * the calendar, in time order.
 */
export function todayMeals(table: TodaysTable, days: SlotDay[]): TodayMeal[] {
  const today = days.filter((day) => day.date === table.date);

  const laid = table.slots.map((slot) => ({
    ...slot,
    sponsors: sponsorsOf(today.find((day) => day.mealSlot === slot.mealSlot)),
  }));

  const offTable = today
    .filter((day) => !table.slots.some((slot) => slot.mealSlot === day.mealSlot) && sponsorsOf(day).length > 0)
    .map(
      (day): TodayMeal => ({
        mealSlot: day.mealSlot,
        name: day.slotName,
        nameSi: day.slotNameSi,
        nameTa: day.slotNameTa,
        startTime: day.startTime,
        maxSponsors: day.maxSponsors,
        bookedSponsors: day.bookedSponsors,
        requested: day.bookings.filter((booking) => booking.status === "Requested").length,
        state: day.status,
        sponsors: sponsorsOf(day),
      }),
    );

  return [...laid, ...offTable].sort((a, b) => a.startTime.localeCompare(b.startTime));
}

/** Served is offered on a confirmed booking only, to someone whose role may edit the calendar. */
export function mayServe(booking: CalendarBooking, can: (code: string) => boolean): boolean {
  return booking.status === "Approved" && can(CALENDAR_EDIT);
}

/** The words for a refusal here. A 403 is this role, not the donor's booking screens' case. */
export function failureKey(failure: BookingFailure): MessageKey {
  return failure.status === 403 ? "Booking.gate.notAllowed" : bookingErrorKey(failure);
}

/** A refusal that means the list on screen is out of date: read it again. */
export function movedOn(failure: BookingFailure): boolean {
  return failure.code === "stale" || failure.code === "invalid_transition" || failure.code === "booking_not_found";
}

/** How many requests a list of slot-days holds. */
export function countRequests(days: SlotDay[]): number {
  return days.reduce((sum, day) => sum + day.bookings.filter((booking) => booking.status === "Requested").length, 0);
}

export type Sentence = { key: MessageKey; values: Record<string, string | number> };

/**
 * What today needs, in one sentence. `waiting` is null when the session may not answer requests:
 * a number it may not see is never in the sentence. `now` is "HH:mm" on Colombo's clock.
 */
export function institutionSentence({
  table,
  waiting,
  language,
  now,
}: {
  table: TodaysTable;
  waiting: number | null;
  language: Language;
  now: string;
}): Sentence {
  const laid = table.slots.filter((slot: TableSlot) => slot.state !== "Closed");
  const next = laid.find((slot) => slot.startTime > now);
  const meal = next ? { meal: inLocale(language, next.name, next.nameSi, next.nameTa), time: next.startTime } : null;

  if (waiting) {
    return meal
      ? { key: "Dashboard.sentence.waitingAndNext", values: { count: waiting, ...meal } }
      : { key: "Dashboard.sentence.waiting", values: { count: waiting } };
  }

  if (laid.length === 0) return { key: "Dashboard.sentence.noMeals", values: {} };
  if (!meal) return { key: "Dashboard.sentence.mealsOver", values: {} };

  // Places still open on the meals yet to come; one already begun can no longer be given.
  const open = laid
    .filter((slot) => slot.startTime > now && slot.state === "Open")
    .reduce((sum, slot) => sum + (slot.maxSponsors - slot.bookedSponsors), 0);

  return open > 0
    ? { key: "Dashboard.sentence.nextWithOpen", values: { ...meal, open } }
    : { key: "Dashboard.sentence.nextAllGiven", values: meal };
}
