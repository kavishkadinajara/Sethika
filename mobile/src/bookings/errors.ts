import { hasMessage, type MessageKey } from "@/i18n/translate";

/*  A refused booking request in the donor's words: the mobile twin of the web's useBookingError.
    The codes are the API's (BookingErrors, BookMealResult, BookingDecisionResult; plan 03 E25),
    each with its own sentence in Booking.errors, copied from web/messages. Anything else falls
    back to the shared Errors.* sentences, then to "something went wrong".                    */

/** What a booking call that did not succeed tells the screen. `status` 0 is a network failure. */
export type BookingFailure = { status: number; code: string | null; fields: string[] };

/** The codes a donor's booking screens can meet, each with a sentence in Booking.errors. */
export const BOOKING_CODES = [
  // a day that cannot be booked
  "slot_inactive",
  "institution_not_active",
  "not_on_this_day",
  "out_of_season",
  "date_blocked",
  "too_soon",
  "too_far",
  "slot_full",
  // the booking itself
  "headcount_out_of_range",
  "fulfilment_not_offered",
  "already_booked",
  "slot_busy",
  "phone_required",
  "slot_not_found",
  "institution_not_found",
  "invalid_details",
  // after it was made
  "booking_not_found",
  "invalid_transition",
  "cancel_window_closed",
  "regular_day",
  "reason_too_long",
  "stale",
] as const;

export type BookingCode = (typeof BOOKING_CODES)[number];

function isBookingCode(code: string | null): code is BookingCode {
  return code !== null && (BOOKING_CODES as readonly string[]).includes(code);
}

/** The message that explains `failure`. */
export function bookingErrorKey(failure: BookingFailure): MessageKey {
  if (failure.status === 0) return "Errors.network";
  if (failure.status === 429) return "Errors.tooManyRequestsNoTime";
  if (isBookingCode(failure.code)) return `Booking.errors.${failure.code}`;

  const shared = `Errors.${failure.code}`;
  if (failure.code && hasMessage(shared)) return shared;

  // A 401 that survived the silent refresh: the session is over. A 403 without a code: the
  // account's permissions do not include booking (an institution session, plan 02 B1).
  if (failure.status === 401) return "Errors.session_expired";
  if (failure.status === 403) return "Discover.sheet.notAllowed";
  return "Errors.unexpected";
}

/**
 * Refusals that mean the day went while the form was open (taken, closed, too late now): the
 * sheet closes and the month is loaded again, so the donor sees the day as it is. A donor who
 * already holds the day (`already_booked`) keeps the sheet and reads why.
 */
export function dayIsGone(code: string | null): boolean {
  return (
    code === "slot_full" ||
    code === "date_blocked" ||
    code === "too_soon" ||
    code === "slot_inactive" ||
    code === "institution_not_active"
  );
}
