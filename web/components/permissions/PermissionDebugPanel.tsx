"use client";

import { useTranslations } from "next-intl";
import { usePermissions } from "@/lib/permissions/usePermissions";

/**
 * Development aid: shows what the provider resolved, so a missing button can be traced to
 * a missing permission code rather than guessed at.
 */
export function PermissionDebugPanel() {
  const { isAuthenticated, isPlatform, institutionId, can } = usePermissions();
  const t = useTranslations("PermissionDebug");

  const probes = [
    "BOOKINGS.CALENDAR.VIEW",
    "BOOKINGS.CALENDAR.APPROVE",
  ];

  return (
    <section className="mt-12 rounded border border-hairline bg-surface p-4 text-sm">
      <h2 className="font-medium text-brand">{t("heading")}</h2>

      <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
        <dt className="text-muted">{t("signedIn")}</dt>
        <dd>{isAuthenticated ? t("yes") : t("no")}</dd>

        <dt className="text-muted">{t("platformStaff")}</dt>
        <dd>{isPlatform ? t("yes") : t("no")}</dd>

        <dt className="text-muted">{t("institution")}</dt>
        <dd>{institutionId ?? t("none")}</dd>
      </dl>

      <ul className="mt-3 space-y-1">
        {probes.map((code) => (
          <li key={code} className="flex items-center gap-2">
            <span className={can(code) ? "text-brand" : "text-muted/70"}>
              {can(code) ? t("granted") : t("denied")}
            </span>
            <code className="text-muted">{code}</code>
          </li>
        ))}
      </ul>

      {!isAuthenticated && (
        <p className="mt-3 text-muted">{t("signedOutNote")}</p>
      )}
    </section>
  );
}
