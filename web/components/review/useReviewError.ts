"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useOnboardingError } from "@/components/onboarding/useOnboardingError";
import type { OnboardingFailure } from "@/lib/onboarding/api";

/** The review's own codes (ReviewResult, RegistrationErrors); the rest are the application's, then the shared ones. */
const reviewCodes = [
  "own_application",
  "checklist_item_unknown",
  "reject_reason_unknown",
  "remarks_required",
  "question_required",
  "document_not_found",
  "documents_not_verified",
  "checklist_incomplete",
] as const;
type ReviewCode = (typeof reviewCodes)[number];

export function useReviewError() {
  const t = useTranslations("Review.errors");
  const onboarding = useOnboardingError();

  return useCallback(
    (failure: OnboardingFailure): string =>
      failure.code && (reviewCodes as readonly string[]).includes(failure.code) ? t(failure.code as ReviewCode) : onboarding(failure),
    [t, onboarding],
  );
}
