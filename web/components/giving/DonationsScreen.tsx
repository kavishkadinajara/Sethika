"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useMemo, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { downloadDonationsCsv, listCounterMethods, listDonations } from "@/lib/giving/api";
import { canVoid, filterDonations, monthOf, shiftMonth, totals, type DonationFilter } from "@/lib/giving/money";
import { csvName } from "@/lib/giving/report";
import type { DonationListItem, LookupItem } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { colomboParts } from "@/lib/services/time";
import { cn, focusRing } from "@/lib/utils";
import { useGiftText } from "./GiftText";
import { RecordDonationSheet } from "./RecordDonationSheet";
import { saveBlob } from "./saveBlob";
import { useGivingError } from "./useGivingError";
import { VoidDonationSheet } from "./VoidDonationSheet";

/**
 * The institution's book of gifts (plan 09, D.1): one Colombo month at a time, newest first, voided
 * gifts kept in the list and marked, and out of the totals (M6). Staff at the counter record a gift
 * (M1); a second person voids one behind a step-up (M6); each gift's receipt opens as a printable
 * page (M4).
 */
export function DonationsScreen() {
  const t = useTranslations("Giving.donations");
  const dates = useDates();
  const errorMessage = useGivingError();
  const { money, method, gift } = useGiftText();
  const { can } = usePermissions();
  const locale = useLocale();
  const canRecord = can("GIVING.DONATIONS.CREATE");
  const canExport = can("GIVING.DONATIONS.EXPORT");
  const canVoidAny = can("GIVING.DONATIONS.DELETE");
  const toast = useToast();

  const thisMonth = useMemo(() => monthOf(colomboParts(new Date().toISOString()).date), []);
  const [month, setMonth] = useState(thisMonth);
  const [rows, setRows] = useState<DonationListItem[] | null>(null);
  const [methods, setMethods] = useState<LookupItem[]>([]);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [filter, setFilter] = useState<DonationFilter>({ method: null, voided: "all" });
  const [recording, setRecording] = useState(false);
  const [voiding, setVoiding] = useState<DonationListItem | null>(null);

  const load = useCallback(async (which: string) => {
    setFailure(null);
    const result = await listDonations(which);
    if (!result.ok) return setFailure(result.failure);
    setRows(result.value);
  }, []);

  useEffect(() => {
    // Loading the month's gifts is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(month);
  }, [load, month]);

  useEffect(() => {
    void listCounterMethods().then((result) => result.ok && setMethods(result.value));
  }, []);

  async function saveCsv() {
    setFailure(null);
    const result = await downloadDonationsCsv(month, locale);
    if (!result.ok) return setFailure(result.failure);
    saveBlob(result.value, csvName("donations", month));
  }

  const shown = useMemo(() => (rows ? filterDonations(rows, filter) : []), [rows, filter]);
  const sums = useMemo(() => totals(rows ?? []), [rows]);

  if (failure && !rows) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load(month)} className={cn(textLink, focusRing, "min-h-0")}>
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

  const methodChips: { value: string | null; label: string }[] = [
    { value: null, label: t("allMethods") },
    ...["CASH", "BANK_TRANSFER", "CHEQUE", "CARD", "PAYHERE"].map((code) => ({ value: code, label: method(code) ?? code })),
  ];
  const voidedChips: { value: DonationFilter["voided"]; label: string }[] = [
    { value: "all", label: t("voidedAll") },
    { value: "kept", label: t("voidedKept") },
    { value: "voided", label: t("voidedOnly") },
  ];

  return (
    <div className="grid gap-6">
      <FormError>{failure && errorMessage(failure)}</FormError>

      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex items-center gap-2" role="group" aria-label={t("month")}>
          <Button variant="outline" onClick={() => setMonth(shiftMonth(month, -1))} aria-label={t("previousMonth")}>
            ‹
          </Button>
          <h2 className="min-w-40 text-center text-xl font-semibold text-brand" aria-live="polite">
            {dates.monthYear(`${month}-01`)}
          </h2>
          <Button variant="outline" onClick={() => setMonth(shiftMonth(month, 1))} disabled={month >= thisMonth} aria-label={t("nextMonth")}>
            ›
          </Button>
        </div>
        <div className="flex flex-wrap gap-3">
          {canExport && (
            <Button variant="outline" onClick={() => void saveCsv()}>
              {t("csv")}
            </Button>
          )}
          {canRecord && <Button onClick={() => setRecording(true)}>{t("record")}</Button>}
        </div>
      </div>

      <p className="text-fg" data-testid="giving-totals">
        {t("totals", { money: money(sums.money), gifts: sums.gifts, items: sums.itemGifts })}
        {sums.voided > 0 && <span className="text-muted"> · {t("voidedCount", { count: sums.voided })}</span>}
      </p>

      {rows.length === 0 ? (
        <EmptyState>{t("none")}</EmptyState>
      ) : (
        <>
          <div className="grid gap-3">
            <Chips label={t("methodLabel")} options={methodChips} value={filter.method} onChange={(m) => setFilter({ ...filter, method: m })} />
            <Chips label={t("voidedLabel")} options={voidedChips} value={filter.voided} onChange={(v) => setFilter({ ...filter, voided: v })} />
          </div>

          {shown.length === 0 ? (
            <p className="text-muted">{t("noMatch")}</p>
          ) : (
            <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3" aria-label={t("list")}>
              {shown.map((row) => (
                <li key={row.publicId}>
                  <article
                    aria-label={row.receiptNo}
                    className={cn("grid h-full content-start gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift", row.isVoided && "opacity-75")}
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <h3 className="min-w-0 break-words text-lg font-semibold leading-snug text-brand">{gift(row)}</h3>
                      {row.isVoided && (
                        <span className="rounded-full border border-danger/60 bg-danger-soft px-3 py-1 text-sm font-semibold text-danger">{t("void")}</span>
                      )}
                    </div>
                    <p className="break-words text-fg">
                      {row.donorName}
                      {row.fromDonorBook && <span className="text-muted"> · {t("fromDonorBook")}</span>}
                    </p>
                    <p className="text-sm text-muted">
                      <span className="tabular-nums">{row.receiptNo}</span> · {dates.medium(colomboParts(row.donatedAt).date)}
                      {row.method && <> · {method(row.method)}</>}
                    </p>
                    <p className="text-sm text-muted">{t("recordedBy", { name: row.recordedByName })}</p>
                    <div className="flex flex-wrap gap-2 pt-1">
                      <Button asChild variant="outline">
                        <Link href={`/receipts/${row.publicId}`} aria-label={t("receiptNamed", { receiptNo: row.receiptNo })}>
                          {t("receipt")}
                        </Link>
                      </Button>
                      {canVoid(row, canVoidAny) && (
                        <Button variant="ghost" onClick={() => setVoiding(row)} aria-label={t("voidNamed", { receiptNo: row.receiptNo })}>
                          {t("voidAction")}
                        </Button>
                      )}
                    </div>
                  </article>
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      {recording && (
        <RecordDonationSheet
          methods={methods}
          onClose={() => setRecording(false)}
          onRecorded={(receiptNo) => {
            setRecording(false);
            toast({ variant: "success", title: t("recorded", { receiptNo }) });
            if (month === thisMonth) void load(month);
            else setMonth(thisMonth);
          }}
        />
      )}

      {voiding && (
        <VoidDonationSheet
          donation={voiding}
          onClose={() => setVoiding(null)}
          onVoided={() => {
            setVoiding(null);
            toast({ variant: "success", title: t("voided", { receiptNo: voiding.receiptNo }) });
            void load(month);
          }}
        />
      )}
    </div>
  );
}

/** A row of pressable chips, one of them on. */
function Chips<V extends string | null>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: V; label: string }[];
  value: V;
  onChange: (value: V) => void;
}) {
  return (
    <div role="group" aria-label={label} className="flex flex-wrap items-center gap-2">
      <span className="text-sm font-medium text-muted">{label}</span>
      {options.map((option) => {
        const on = option.value === value;
        return (
          <button
            key={option.value ?? "all"}
            type="button"
            aria-pressed={on}
            onClick={() => onChange(option.value)}
            className={cn(
              "min-h-11 rounded-full border px-4 text-sm font-medium transition-colors",
              on ? "border-primary bg-primary text-on-primary" : "border-hairline bg-surface text-fg hover:border-line",
              focusRing,
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
