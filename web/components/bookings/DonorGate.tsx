"use client";

import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { textLink } from "@/components/auth/links";
import { Notice } from "@/components/onboarding/ApplyGate";
import { PersonalSwitch } from "@/components/shell/PersonalSwitch";
import { Spinner } from "@/components/ui/spinner";
import { Link, usePathname } from "@/i18n/navigation";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";

/**
 * A donor's own screens: signed in, in their personal session (the DONOR role holds only there,
 * plan 02 B1), with `permission`. The API checks all of this again.
 */
export function DonorGate({ permission, children }: { permission: string; children: ReactNode }) {
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
    const next = `?next=${encodeURIComponent(pathname)}`;
    return (
      <Notice>
        <p>{t("donorSignIn")}</p>
        <p className="mt-2 flex flex-wrap gap-x-4">
          <Link href={`/login${next}`} className={cn(textLink, focusRing)}>
            {t("signIn")}
          </Link>
          <Link href={`/signup${next}`} className={cn(textLink, focusRing)}>
            {t("createAccount")}
          </Link>
        </p>
      </Notice>
    );
  }

  if (session.activeInstitution)
    return (
      <Notice>
        <p>{t("institutionSession")}</p>
        <PersonalSwitch className="mt-4" />
      </Notice>
    );
  if (!can(permission)) return <Notice>{t("notAllowed")}</Notice>;

  return <>{children}</>;
}
