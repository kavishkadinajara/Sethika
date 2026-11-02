"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useApiErrorMessage } from "@/components/auth/useApiErrorMessage";
import type { BookingFailure } from "@/lib/bookings/api";

/** The booking codes with their own message (BookingErrors, BookMealResult, SlotChangeResult, ...). */
const bookingCodes = [
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
  // after it was made
  "booking_not_found",
  "invalid_transition",
  "cancel_window_closed",
  "too_early",
  "reason_required",
  "reason_too_long",
  "stale",
  // yearly bookings and the memorial planner (plan 07)
  "recurring_not_found",
  "memorial_not_found",
  "memorial_plan_not_found",
  "not_an_observance",
  // sharing a booking (co-sponsors)
  "booking_closed",
  "too_many_invitations",
  "invitation_not_found",
  "invitation_answered",
  "link_not_found",
  "own_booking",
  "already_joined",
  // the institution's slots
  "slot_name_taken",
  "blocked_date_not_found",
  "no_active_slot",
  "invalid_range",
  "invalid_details",
] as const;
type BookingCode = (typeof bookingCodes)[number];

/** One translated sentence for a booking failure; anything else goes to the shared messages. */
export function useBookingError() {
  const t = useTranslations("Booking.errors");
  const shared = useApiErrorMessage();

  return useCallback(
    (failure: BookingFailure): string =>
      failure.code && (bookingCodes as readonly string[]).includes(failure.code)
        ? t(failure.code as BookingCode)
        : shared(failure),
    [t, shared],
  );
}
