"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useApiErrorMessage } from "@/components/auth/useApiErrorMessage";
import type { OnboardingFailure } from "@/lib/onboarding/api";

/** The onboarding codes with their own message (ApplicationResult, RegistrationErrors, UploadedFileRules). */
const onboardingCodes = [
  "registration_not_found",
  "too_many_applications",
  "stale",
  "document_type_not_allowed",
  "declaration_required",
  "file_empty",
  "file_too_large",
  "file_type_not_allowed",
  "file_content_mismatch",
  "questions_unanswered",
  "office_bearer_duplicate",
  "registration_no_in_use",
  "application_not_editable",
  "documents_missing",
  "office_bearer_required",
  "signatory_required",
  "invalid_transition",
  "invalid_details",
] as const;
type OnboardingCode = (typeof onboardingCodes)[number];

/** One translated sentence for an onboarding failure; anything else goes to the shared messages. */
export function useOnboardingError() {
  const t = useTranslations("Apply.errors");
  const shared = useApiErrorMessage();

  return useCallback(
    (failure: OnboardingFailure): string =>
      failure.code && (onboardingCodes as readonly string[]).includes(failure.code)
        ? t(failure.code as OnboardingCode)
        : shared(failure),
    [t, shared],
  );
}
