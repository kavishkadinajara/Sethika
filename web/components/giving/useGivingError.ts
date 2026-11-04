"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useBookingError } from "@/components/bookings/useBookingError";
import type { BookingFailure } from "@/lib/bookings/api";

/** The giving codes with their own message (DonationResult, DonationErrors, plan 09). */
const givingCodes = [
  "invalid_details",
  "donation_not_found",
  "donor_not_found",
  "stale",
  "already_void",
  "void_own_record",
  "invalid_month",
] as const;
type GivingCode = (typeof givingCodes)[number];

/** One translated sentence for a giving failure; anything else goes to the booking messages (session, network). */
export function useGivingError() {
  const t = useTranslations("Giving.errors");
  const shared = useBookingError();

  return useCallback(
    (failure: BookingFailure): string =>
      failure.code && (givingCodes as readonly string[]).includes(failure.code) ? t(failure.code as GivingCode) : shared(failure),
    [t, shared],
  );
}
