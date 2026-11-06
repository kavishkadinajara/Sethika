"use client";

import { useLocale, useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { Link } from "@/i18n/navigation";
import { inLocale } from "@/lib/bookings/names";
import { listMyDonations, listMyPledges } from "@/lib/giving/api";
import { formatAmount, formatQuantity } from "@/lib/giving/money";
import { openPledges, recentGifts } from "@/lib/giving/report";
import type { MyDonation, MyPledge } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { colomboParts } from "@/lib/services/time";
import { cn, focusRing } from "@/lib/utils";
import { Panel, PanelEmpty, PanelPlaceholder } from "./ledger-ui";

/** How many gifts and how many pledges the panel names before sending the rest to My giving. */
const SHOWN = 3;

/**
 * "My giving" on the donor's home (plan 09, M18): the latest gifts with their receipts, and the pledges
 * still owed. Its reads are not sent without `MYGIVING.DONATIONS.VIEW`.
 */
export function MyGivingPanel() {
  const t = useTranslations("Dashboard.myGiving");
  const locale = useLocale();
  const dates = useDates();
  const { can } = usePermissions();
  const reads = can("MYGIVING.DONATIONS.VIEW");
  const [gifts, setGifts] = useState<MyDonation[] | null>(null);
  const [pledges, setPledges] = useState<MyPledge[] | null>(null);

  useEffect(() => {
    if (!reads) return;
    let cancelled = false;
    void Promise.all([listMyDonations(), listMyPledges()]).then(([donations, promised]) => {
      if (cancelled) return;
      if (donations.ok) setGifts(recentGifts(donations.value, SHOWN));
      if (promised.ok) setPledges(openPledges(promised.value, SHOWN));
    });
    return () => {
      cancelled = true;
    };
  }, [reads]);

  if (!reads) return null;

  const place = (row: { institutionName: string; institutionNameSi: string | null; institutionNameTa: string | null }) =>
    inLocale(locale, row.institutionName, row.institutionNameSi, row.institutionNameTa);

  return (
    <Panel title={t("title")} link={{ href: "/giving/mine", label: t("all") }}>
      {gifts === null || pledges === null ? (
        <PanelPlaceholder rows={2} />
      ) : gifts.length === 0 && pledges.length === 0 ? (
        <PanelEmpty>{t("none")}</PanelEmpty>
      ) : (
        <div className="grid gap-4">
          {gifts.length > 0 && (
            <ul className="grid grid-cols-1 divide-y divide-hairline" aria-label={t("gifts")}>
              {gifts.map((row) => (
                <li key={row.publicId} className="grid gap-0.5 py-2 first:pt-0 last:pb-0">
                  <span className="min-w-0 break-words font-medium text-fg">
                    {row.kind === "Money" ? t("amount", { amount: formatAmount(row.amount ?? 0) }) : row.items}
                  </span>
                  <span className="min-w-0 break-words text-sm text-muted">
                    {place(row)} · {dates.medium(colomboParts(row.donatedAt).date)}
                  </span>
                  <Link
                    href={`/receipts/${row.publicId}`}
                    aria-label={t("receiptNamed", { receiptNo: row.receiptNo })}
                    className={cn(textLink, focusRing, "min-h-0 justify-self-start text-sm tabular-nums")}
                  >
                    {row.receiptNo}
                  </Link>
                </li>
              ))}
            </ul>
          )}
          {pledges.length > 0 && (
            <div className="grid gap-2">
              <h3 className="font-semibold text-fg">{t("pledges")}</h3>
              <ul className="grid grid-cols-1 divide-y divide-hairline" aria-label={t("pledges")}>
                {pledges.map((row) => (
                  <li key={row.publicId} className="grid gap-0.5 py-2 first:pt-0 last:pb-0">
                    <span className="min-w-0 break-words font-medium text-fg">{row.needTitle}</span>
                    <span className="min-w-0 break-words text-sm text-muted">
                      {place(row)} ·{" "}
                      {row.expectedDate
                        ? t("pledgedBy", { count: formatQuantity(row.quantity), date: dates.medium(row.expectedDate) })
                        : t("pledged", { count: formatQuantity(row.quantity) })}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </Panel>
  );
}
