"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { getGivingSummary } from "@/lib/giving/api";
import { formatAmount } from "@/lib/giving/money";
import type { GivingSummary } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { onShellStale } from "@/lib/shell/summary";
import { Panel, PanelPlaceholder } from "./ledger-ui";

/**
 * The giving line on the institution's Day Ledger (plan 09, M18): this month's money, open needs and
 * pledges due. Its query is not sent without `GIVING.DONATIONS.VIEW`, and its link goes to the report
 * for those who may read it, or else to the gifts.
 */
export function GivingLine() {
  const t = useTranslations("Dashboard.giving");
  const { can } = usePermissions();
  const reads = can("GIVING.DONATIONS.VIEW");
  const [summary, setSummary] = useState<GivingSummary | null>(null);

  const load = useCallback(async () => {
    if (!reads) return;
    const result = await getGivingSummary();
    if (result.ok) setSummary(result.value);
  }, [reads]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
    return onShellStale(() => void load());
  }, [load]);

  if (!reads) return null;

  const link = can("GIVING.REPORTS.VIEW") ? { href: "/reports", label: t("openReport") } : { href: "/giving/donations", label: t("openDonations") };

  return (
    <Panel title={t("title")} link={link}>
      {summary === null ? (
        <PanelPlaceholder rows={1} />
      ) : (
        <dl className="grid grid-cols-1 gap-4 sm:grid-cols-3" data-testid="giving-line">
          <Figure label={t("money")} value={t("amount", { amount: formatAmount(summary.money) })} />
          <Figure label={t("openNeeds")} value={String(summary.openNeeds)} />
          <Figure label={t("pledgesDue")} value={String(summary.pledgesDue)} />
        </dl>
      )}
    </Panel>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid min-w-0 gap-0.5">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="break-words text-xl font-semibold tabular-nums text-fg">{value}</dd>
    </div>
  );
}
