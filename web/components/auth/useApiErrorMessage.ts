"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import type { ApiFailure } from "@/lib/auth/api";
import { formatWait } from "./useCountdown";

/** Error codes the API returns (ApiErrors.cs and the endpoints) that have their own message. */
const knownCodes = [
  "invalid_phone_number",
  "invalid_full_name",
  "invalid_email",
  "password_rejected",
  "consent_missing",
  "phone_not_verified",
  "already_registered",
  "verification_unavailable",
  "invalid_credentials",
  "account_not_active",
  "otp_not_found",
  "otp_attempts_exhausted",
  "no_verified_phone",
  "institution_not_available",
  "session_expired",
] as const;
type KnownCode = (typeof knownCodes)[number];

/**
 * Turns any API failure into one translated sentence. Unknown codes and unexpected statuses
 * get a generic message rather than leaking an English code to the screen.
 */
export function useApiErrorMessage() {
  const t = useTranslations("Errors");

  return useCallback(
    (failure: ApiFailure): string => {
      // No answer at all, or a 5xx without the API's { code } (the proxy could not reach it).
      if (failure.status === 0 || (failure.status >= 500 && failure.code === null)) return t("network");
      if (failure.status === 429) {
        return failure.retryAfter
          ? t("tooManyRequests", { time: formatWait(failure.retryAfter) })
          : t("tooManyRequestsNoTime");
      }
      if (failure.code === "otp_incorrect") {
        return t("otp_incorrect", { count: failure.attemptsRemaining ?? 0 });
      }
      if (failure.code && (knownCodes as readonly string[]).includes(failure.code)) {
        return t(failure.code as KnownCode);
      }
      return t("unexpected");
    },
    [t],
  );
}
