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
import { useSupportMode } from "./SupportBanner";

/**
 * The platform's own screens (plan 04, Y5): signed in, outside any institution (platform grants
 * hold only in a personal session, as a donor's do), holding `permission`. The API checks it again.
 */
export function AdminGate({ permission, children }: { permission: string; children: ReactNode }) {
  const t = useTranslations("Admin.gate");
  const session = useSession();
  const { can, isLoading } = usePermissions();
  const support = useSupportMode();
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

  if (support) return <Notice>{t("inInstitution")}</Notice>;
  if (!can(permission)) return <Notice>{t("notAllowed")}</Notice>;

  return <>{children}</>;
}
