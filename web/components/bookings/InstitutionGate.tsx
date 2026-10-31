"use client";

import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { textLink } from "@/components/auth/links";
import { Notice } from "@/components/onboarding/ApplyGate";
import { Spinner } from "@/components/ui/spinner";
import { Link, usePathname } from "@/i18n/navigation";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";

/**
 * Shows an institution's booking screens only to its staff, acting for it, with `permission`.
 * The API checks all of this again; this only spares them a screen of refusals.
 */
export function InstitutionGate({ permission, children }: { permission: string; children: ReactNode }) {
  const t = useTranslations("Booking.gate");
  const session = useSession();
  const { can, isLoading } = usePermissions();
  const pathname = usePathname();

  if (session.status === "loading" || (session.status === "authenticated" && isLoading)) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  if (session.status !== "authenticated") {
    return (
      <Notice>
        <p>{t("signInFirst")}</p>
        <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={cn(textLink, focusRing)}>
          {t("signIn")}
        </Link>
      </Notice>
    );
  }

  if (!session.activeInstitution) {
    return (
      <Notice>
        <p>{t("chooseInstitution")}</p>
        <Link href={`/select-institution?next=${encodeURIComponent(pathname)}`} className={cn(textLink, focusRing)}>
          {t("choose")}
        </Link>
      </Notice>
    );
  }

  if (!can(permission)) return <Notice>{t("notAllowed")}</Notice>;

  return <>{children}</>;
}
