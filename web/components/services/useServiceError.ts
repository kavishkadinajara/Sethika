"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useApiErrorMessage } from "@/components/auth/useApiErrorMessage";
import type { BookingFailure } from "@/lib/bookings/api";

/** The service codes with their own message (ServiceErrors, RequestServiceResult, AppointmentResult, SetupChangeResult). */
const serviceCodes = [
  // asking
  "service_not_found",
  "service_inactive",
  "institution_not_active",
  "not_on_the_grid",
  "location_not_offered",
  "address_required",
  "too_soon",
  "too_far",
  "phone_required",
  "invalid_details",
  // after it was asked for
  "appointment_not_found",
  "invalid_transition",
  "cancel_window_closed",
  "too_early",
  "already_over",
  "reason_required",
  "reason_too_long",
  "stale",
  // leaders
  "leader_not_free",
  "try_again",
  "already_assigned",
  "too_many_leaders",
  "not_assigned",
  "leader_unavailable",
  "not_a_leader",
  // the institution's set-up
  "service_name_taken",
  "service_type_not_found",
  "leader_not_found",
  "time_off_not_found",
  "account_not_found",
  "account_already_linked",
  "time_off_clashes",
  "time_off_overlaps",
  "invalid_view",
] as const;
type ServiceCode = (typeof serviceCodes)[number];

/** One translated sentence for a service failure; anything else goes to the shared messages. */
export function useServiceError() {
  const t = useTranslations("Services.errors");
  const shared = useApiErrorMessage();

  return useCallback(
    (failure: BookingFailure): string =>
      failure.code && (serviceCodes as readonly string[]).includes(failure.code)
        ? t(failure.code as ServiceCode)
        : shared(failure),
    [t, shared],
  );
}
