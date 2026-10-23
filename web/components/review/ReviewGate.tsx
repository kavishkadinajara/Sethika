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
 * Shows the review screens only to someone who can use them: signed in, holding the reviewer's
 * permission (platform staff). The API checks it on every call; this spares a screen of refusals.
 */
export function ReviewGate({ children }: { children: ReactNode }) {
  const t = useTranslations("Review");
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
        <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={cn(textLink, focusRing, "mt-2")}>
          {t("signIn")}
        </Link>
      </Notice>
    );
  }

  if (!can("ONBOARDING.REQUESTS.VIEW")) return <Notice>{t("notAllowed")}</Notice>;

  return <>{children}</>;
}
