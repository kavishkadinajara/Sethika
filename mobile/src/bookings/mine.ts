import type { DonorBooking } from "./types";

/*  The donor's bookings in the two lists My bookings shows (plan 03, 5.7): what is coming,
    soonest first, and what has been, latest first. A booking is "coming" while it may still
    happen: on or after today (Colombo) and still Requested or Approved. A rejected or cancelled
    booking for next week is history, not something to look forward to.                       */

export function isUpcoming(booking: DonorBooking, today: string): boolean {
  return booking.date >= today && (booking.status === "Requested" || booking.status === "Approved");
}

/** A regular day may be let go while it can still happen, today included (plan 08, K24). */
export function canLetGoOwnDay(booking: DonorBooking, today: string): boolean {
  return booking.regular && isUpcoming(booking, today);
}

export function splitBookings(bookings: readonly DonorBooking[], today: string): { upcoming: DonorBooking[]; past: DonorBooking[] } {
  const soonest = (a: DonorBooking, b: DonorBooking) =>
    a.date.localeCompare(b.date) || a.startTime.localeCompare(b.startTime) || a.bookingNo.localeCompare(b.bookingNo);

  return {
    upcoming: bookings.filter((booking) => isUpcoming(booking, today)).sort(soonest),
    past: bookings.filter((booking) => !isUpcoming(booking, today)).sort((a, b) => soonest(b, a)),
  };
}

export type DonorStats = {
  /** Bookings still to come. */
  upcoming: number;
  /** Meals the institution marked served (Completed). */
  given: number;
  /** The people those served meals fed: their headcounts added up. */
  fed: number;
  /** The soonest booking still to come, if any. */
  next: DonorBooking | null;
};

/**
 * What the donor's own bookings add up to, for their Profile. Only a served meal counts as given:
 * an approved booking may still be cancelled, and a no-show fed no one.
 */
export function donorStats(bookings: readonly DonorBooking[], today: string): DonorStats {
  const { upcoming } = splitBookings(bookings, today);
  const served = bookings.filter((booking) => booking.status === "Completed");
  return {
    upcoming: upcoming.length,
    given: served.length,
    fed: served.reduce((sum, booking) => sum + booking.headcount, 0),
    next: upcoming[0] ?? null,
  };
}
