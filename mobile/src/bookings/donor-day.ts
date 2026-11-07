import type { SessionState } from "@/auth/session-client";
import type { Language, MessageKey } from "@/i18n/translate";

import { addDays } from "./calendar";
import { inLocale } from "./names";
import type { Sentence } from "./today";
import type { DonorBooking } from "./types";

/*  The donor's Day Ledger on Explore (F22), framework-free so every case is unit-tested. The
    sentence is a port of donorSentence in web/lib/dashboard/sentence.ts: a donor reads the same
    words on both. `date` stays a date in the values; the screen puts it into words.            */

export const OWN_BOOKINGS_VIEW = "MYBOOKINGS.BOOKINGS.VIEW";

/** How many meals *Your table* lists on a phone (F22). */
export const TABLE_SHOWN = 3;

/** The account's own bookings are read only for someone acting as themselves who may read them. */
export function mayReadOwnBookings(session: SessionState, can: (code: string) => boolean): boolean {
  return session.status === "authenticated" && session.activeInstitution === null && can(OWN_BOOKINGS_VIEW);
}

/** A donor's meals still to give, soonest first: requested or approved, not yet begun. */
export function mealsToGive(bookings: readonly DonorBooking[], today: string, now: string): DonorBooking[] {
  return bookings
    .filter(
      (booking) =>
        (booking.status === "Requested" || booking.status === "Approved") &&
        (booking.date > today || (booking.date === today && booking.startTime > now)),
    )
    .sort((a, b) => a.date.localeCompare(b.date) || a.startTime.localeCompare(b.startTime));
}

/**
 * The donor's next meal in one sentence. `bookings` is null when they could not be read, or may not
 * be: the invitation then, never "nothing booked", which would be a claim about data not seen.
 */
export function donorSentence({
  bookings,
  language,
  today,
  now,
}: {
  bookings: readonly DonorBooking[] | null;
  language: Language;
  today: string;
  now: string;
}): Sentence {
  if (bookings === null) return { key: "Dashboard.sentence.donorPlain", values: {} };

  const next = mealsToGive(bookings, today, now)[0];
  if (!next) return { key: "Dashboard.sentence.nothingBooked", values: {} };

  const values = {
    meal: inLocale(language, next.slotName, next.slotNameSi, next.slotNameTa),
    place: inLocale(language, next.institutionName, next.institutionNameSi, next.institutionNameTa),
    time: next.startTime,
    date: next.date,
  };
  const when = next.date === today ? "Today" : next.date === addDays(today, 1) ? "Tomorrow" : "Later";
  const key = `Dashboard.sentence.${next.status === "Requested" ? "asked" : "giving"}${when}` as MessageKey;
  return { key, values };
}
