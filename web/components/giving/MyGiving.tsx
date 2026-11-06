"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import { listMyDonations } from "@/lib/giving/api";
import type { MyDonation } from "@/lib/giving/types";
import { colomboParts } from "@/lib/services/time";
import { cn, focusRing } from "@/lib/utils";
import { useGiftText } from "./GiftText";
import { useGivingError } from "./useGivingError";

/**
 * A donor's own giving (plan 09, M18): every gift recorded for them, in any institution, newest first,
 * each with its receipt. Gifts recorded against a donor book row come here once the row is linked to
 * the account (M3, K36). A voided gift stays, marked.
 */
export function MyGiving() {
  const t = useTranslations("Giving.mine");
  const locale = useLocale();
  const dates = useDates();
  const errorMessage = useGivingError();
  const { gift } = useGiftText();
  const [rows, setRows] = useState<MyDonation[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await listMyDonations();
    if (!result.ok) return setFailure(result.failure);
    setRows(result.value);
  }, []);

  useEffect(() => {
    // Loading the donor's gifts is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (failure) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!rows) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  if (rows.length === 0) return <EmptyState>{t("none")}</EmptyState>;

  return (
    <ul className="grid gap-3 lg:grid-cols-2" aria-label={t("list")}>
      {rows.map((row) => (
        <li key={row.publicId}>
          <article
            aria-label={row.receiptNo}
            className={cn("grid h-full content-start gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift", row.isVoided && "opacity-75")}
          >
            <div className="flex flex-wrap items-start justify-between gap-2">
              <h2 className="min-w-0 break-words text-lg font-semibold leading-snug text-brand">{gift(row)}</h2>
              {row.isVoided && (
                <span className="rounded-full border border-danger/60 bg-danger-soft px-3 py-1 text-sm font-semibold text-danger">{t("void")}</span>
              )}
            </div>
            <p className="break-words text-fg">
              <Link href={`/i/${row.institutionSlug}`} className={cn(textLink, focusRing, "min-h-0")}>
                {inLocale(locale, row.institutionName, row.institutionNameSi, row.institutionNameTa)}
              </Link>
            </p>
            <p className="text-sm text-muted">
              <span className="tabular-nums">{row.receiptNo}</span> · {dates.medium(colomboParts(row.donatedAt).date)}
            </p>
            <div className="pt-1">
              <Button asChild variant="outline">
                <Link href={`/receipts/${row.publicId}`} aria-label={t("receiptNamed", { receiptNo: row.receiptNo })}>
                  {t("receipt")}
                </Link>
              </Button>
            </div>
          </article>
        </li>
      ))}
    </ul>
  );
}
