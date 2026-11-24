"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { useApiErrorMessage } from "@/components/auth/useApiErrorMessage";
import type { AccessFailure } from "@/lib/access/api";

/** The role and invitation codes with their own message (InstitutionRoleEndpoints.Refused). */
const accessCodes = [
  "account_not_found",
  "already_member",
  "invitation_pending",
  "invitation_closed",
  "invitation_expired",
  "institution_not_active",
  "invitation_not_found",
  "own_roles",
  "rank_not_junior",
  "no_rank",
  "outside_ceiling",
  "not_held",
  "unknown_permission",
  "role_exists",
  "invalid_role_code",
  "invalid_role_name",
  "unknown_level",
  "role_not_found",
  "member_not_found",
  "assignment_not_found",
  "invalid_phone_number",
  "changed_meanwhile",
  "invalid_version",
  "last_owner",
] as const;
type AccessCode = (typeof accessCodes)[number];

/** One translated sentence for an access failure; anything else goes to the shared messages. */
export function useAccessError() {
  const t = useTranslations("Access.errors");
  const shared = useApiErrorMessage();

  return useCallback(
    (failure: AccessFailure): string =>
      failure.code && (accessCodes as readonly string[]).includes(failure.code) ? t(failure.code as AccessCode) : shared(failure),
    [t, shared],
  );
}
