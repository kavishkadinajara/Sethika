"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useApiErrorMessage } from "@/components/auth/useApiErrorMessage";
import type { BookingFailure } from "@/lib/bookings/api";

/** The donor book's codes with their own message (DonorChangeResult, plan 08 K1). */
const sponsorCodes = ["invalid_details", "donor_not_found", "donor_phone_taken", "donor_account_taken", "stale"] as const;
type SponsorCode = (typeof sponsorCodes)[number];

/** One translated sentence for a donor book failure; anything else goes to the shared messages. */
export function useSponsorError() {
  const t = useTranslations("Sponsors.errors");
  const shared = useApiErrorMessage();

  return useCallback(
    (failure: BookingFailure): string =>
      failure.code && (sponsorCodes as readonly string[]).includes(failure.code) ? t(failure.code as SponsorCode) : shared(failure),
    [t, shared],
  );
}
