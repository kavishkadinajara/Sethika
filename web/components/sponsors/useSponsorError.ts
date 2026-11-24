"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useBookingError } from "@/components/bookings/useBookingError";
import type { BookingFailure } from "@/lib/bookings/api";

/** The donor book's and the arrangements' codes with their own message (DonorChangeResult, SponsorshipChangeResult, plan 08). */
const sponsorCodes = [
  "invalid_details",
  "donor_not_found",
  "donor_phone_taken",
  "donor_account_taken",
  "stale",
  "sponsorship_not_found",
  "sponsorship_ended",
  "day_already_changed",
  "day_not_booked",
] as const;
type SponsorCode = (typeof sponsorCodes)[number];

/** One translated sentence for a donor book failure; anything else goes to the booking messages (an engine refusal in a swap). */
export function useSponsorError() {
  const t = useTranslations("Sponsors.errors");
  const shared = useBookingError();

  return useCallback(
    (failure: BookingFailure): string =>
      failure.code && (sponsorCodes as readonly string[]).includes(failure.code) ? t(failure.code as SponsorCode) : shared(failure),
    [t, shared],
  );
}
