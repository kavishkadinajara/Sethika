"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useApiErrorMessage } from "@/components/auth/useApiErrorMessage";
import type { AdminFailure } from "@/lib/admin/api";

/** The administration codes with their own message (AdminEndpoints.cs). */
const adminCodes = [
  "already_registered",
  "invalid_phone_number",
  "invalid_full_name",
  "own_account",
  "rank_not_junior",
  "user_not_found",
  "role_not_found",
  "institution_not_found",
] as const;
type AdminCode = (typeof adminCodes)[number];

/** One translated sentence for an administration failure; anything else goes to the shared messages. */
export function useAdminError() {
  const t = useTranslations("Admin.errors");
  const shared = useApiErrorMessage();

  return useCallback(
    (failure: AdminFailure): string =>
      failure.code && (adminCodes as readonly string[]).includes(failure.code) ? t(failure.code as AdminCode) : shared(failure),
    [t, shared],
  );
}
