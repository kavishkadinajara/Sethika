"use client";

import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { textLink } from "@/components/auth/links";
import { PersonalSwitch } from "@/components/shell/PersonalSwitch";
import { Spinner } from "@/components/ui/spinner";
import { Link, usePathname } from "@/i18n/navigation";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";

/**
 * Shows the application screens only to someone who can use them: signed in, in a personal
 * session (the applicant's permissions hold only there, plan 02 B1), with the permission. The API
 * checks all of this again; this only spares the applicant a screen of refusals.
 */
export function ApplyGate({ children }: { children: ReactNode }) {
  const t = useTranslations("Apply");
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
        <p>{t("signInFirst")}</p>
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

  // Applying is the person's own business, so it happens in their own account (plan 02, B1);
  // the switch back to it is one click (plan 04, P1).
  if (session.activeInstitution)
    return (
      <Notice>
        <p>{t("institutionSession")}</p>
        <PersonalSwitch className="mt-4" />
      </Notice>
    );

  if (!can("ONBOARDING.APPLICATION.VIEW")) return <Notice>{t("notAllowed")}</Notice>;

  return <>{children}</>;
}

export function Notice({ children }: { children: ReactNode }) {
  return <div className="rounded-cup border border-hairline bg-surface p-5 text-fg">{children}</div>;
}
