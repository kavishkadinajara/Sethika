"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import { cancelMyPledge, listMyPledges } from "@/lib/giving/api";
import { formatQuantity } from "@/lib/giving/money";
import type { MyPledge } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { useNeedLookups } from "./NeedText";
import { useGivingError } from "./useGivingError";

/**
 * A donor's pledges in My giving (plan 09, §5): open ones first, each cancellable until it is delivered.
 * Delivered ones link their receipt; one staff cancelled says why. With none yet, it points at the board.
 */
export function MyPledges() {
  const t = useTranslations("Giving.myPledges");
  const locale = useLocale();
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useGivingError();
  const { unit } = useNeedLookups();
  const { can } = usePermissions();
  const canCancel = can("MYGIVING.DONATIONS.EDIT");
  const [rows, setRows] = useState<MyPledge[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const load = useCallback(async () => {
    const result = await listMyPledges();
    if (!result.ok) return setFailure(result.failure);
    setRows(result.value);
  }, []);

  useEffect(() => {
    // Loading the donor's pledges is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  async function cancel(row: MyPledge) {
    setFailure(null);
    const result = await cancelMyPledge(row.publicId);
    if (!result.ok) return setFailure(result.failure);
    toast({ variant: "success", title: t("cancelled") });
    void load();
  }

  return (
    <section className="grid gap-3" aria-labelledby="my-pledges">
      <h2 id="my-pledges" className="text-xl font-semibold text-brand">
        {t("title")}
      </h2>
      <FormError>{failure && errorMessage(failure)}</FormError>

      {rows?.length === 0 && (
        <p className="text-muted">
          {t("none")}{" "}
          <Link href="/needs" className={cn(textLink, focusRing, "min-h-0")}>
            {t("seeBoard")}
          </Link>
        </p>
      )}

      {rows && rows.length > 0 && (
        <ul className="grid gap-3 lg:grid-cols-2" aria-label={t("list")}>
          {rows.map((row) => (
            <li key={row.publicId}>
              <article aria-label={row.needTitle} className="grid h-full content-start gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <h3 className="min-w-0 break-words text-lg font-semibold leading-snug text-brand">{row.needTitle}</h3>
                  <span className="text-sm font-medium text-muted">{t(`status.${row.status}`)}</span>
                </div>
                <p className="break-words text-fg">
                  <Link href={`/i/${row.institutionSlug}`} className={cn(textLink, focusRing, "min-h-0")}>
                    {inLocale(locale, row.institutionName, row.institutionNameSi, row.institutionNameTa)}
                  </Link>
                </p>
                <p className="text-sm text-muted">
                  {t("promised", { count: formatQuantity(row.quantity), unit: unit(row.unit) })}
                  {row.expectedDate && <> · {t("expected", { date: dates.medium(row.expectedDate) })}</>}
                </p>
                {row.status === "Delivered" && row.receivedQuantity !== null && (
                  <p className="text-sm text-fg">{t("came", { count: formatQuantity(row.receivedQuantity), unit: unit(row.unit) })}</p>
                )}
                {row.cancelReason && <p className="break-words text-sm text-muted">{t("reasonShown", { reason: row.cancelReason })}</p>}
                <div className="flex flex-wrap gap-2 pt-1">
                  {row.receiptPublicId && row.receiptNo && (
                    <Button asChild variant="outline">
                      <Link href={`/receipts/${row.receiptPublicId}`} aria-label={t("receiptNamed", { receiptNo: row.receiptNo })}>
                        {t("receipt")}
                      </Link>
                    </Button>
                  )}
                  {row.status === "Pledged" && canCancel && (
                    <Button variant="ghost" onClick={() => void cancel(row)} aria-label={t("cancelNamed", { need: row.needTitle })}>
                      {t("cancel")}
                    </Button>
                  )}
                </div>
              </article>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
