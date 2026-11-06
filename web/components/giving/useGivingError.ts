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
  "need_not_found",
  "pledge_not_found",
  "need_full",
  "need_closed",
  "need_wrong_status",
  "quantity_below_pledged",
  "open_pledges",
  "need_has_gifts",
  "pledge_not_open",
  "too_many_pledges",
  "online_giving_off",
  "payment_not_found",
  "phone_required",
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
