import type { DonorBooking } from "./types";

/*  The donor's bookings in the two lists My bookings shows (plan 03, 5.7): what is coming,
    soonest first, and what has been, latest first. A booking is "coming" while it may still
    happen: on or after today (Colombo) and still Requested or Approved. A rejected or cancelled
    booking for next week is history, not something to look forward to.                       */

export function isUpcoming(booking: DonorBooking, today: string): boolean {
  return booking.date >= today && (booking.status === "Requested" || booking.status === "Approved");
}

export function splitBookings(bookings: readonly DonorBooking[], today: string): { upcoming: DonorBooking[]; past: DonorBooking[] } {
  const soonest = (a: DonorBooking, b: DonorBooking) =>
    a.date.localeCompare(b.date) || a.startTime.localeCompare(b.startTime) || a.bookingNo.localeCompare(b.bookingNo);

  return {
    upcoming: bookings.filter((booking) => isUpcoming(booking, today)).sort(soonest),
    past: bookings.filter((booking) => !isUpcoming(booking, today)).sort((a, b) => soonest(b, a)),
  };
}
