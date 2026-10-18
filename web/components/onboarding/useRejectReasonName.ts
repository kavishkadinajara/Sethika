"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";

/** The seeded REJECT_REASON codes (12_OnboardingDrafts.sql), which have words in every language. */
const reasonCodes = ["DOCUMENTS_INCOMPLETE", "NOT_VERIFIABLE", "DUPLICATE", "NOT_ELIGIBLE", "OTHER"] as const;
type ReasonCode = (typeof reasonCodes)[number];

/** A reject reason in the screen's language; a code added later shows the API's name, or itself. */
export function useRejectReasonName() {
  const t = useTranslations("Apply.rejectReasons");
  return useCallback(
    (code: string, fallback?: string | null): string =>
      (reasonCodes as readonly string[]).includes(code) ? t(code as ReasonCode) : (fallback ?? code),
    [t],
  );
}
