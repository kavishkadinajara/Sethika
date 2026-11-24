"use client";

import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { DonorDay } from "./DonorDay";
import { InstitutionDay } from "./InstitutionDay";
import { PlatformDay } from "./PlatformDay";

/**
 * The signed-in home page: the Day Ledger of the session's kind (plan 05, R4). Inside an
 * institution, its day; in a platform session, the platform's; otherwise the donor's. The
 * sidebar holds the way to every screen, so the dashboard says what today needs instead.
 */
export function Dashboard() {
  const state = useSession();
  const { isPlatform } = usePermissions();

  const institution =
    state.status === "authenticated" && state.activeInstitution !== null
      ? state.institutions.find((i) => i.slug === state.activeInstitution)
      : undefined;

  // Keyed by the context, so a switch starts the new day afresh instead of showing the old one's.
  if (institution) return <InstitutionDay key={institution.slug} name={institution.name} />;
  return isPlatform ? <PlatformDay /> : <DonorDay />;
}
