"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import { downloadReportCsv, getMonthlyReport } from "@/lib/giving/api";
import { monthOf, shiftMonth } from "@/lib/giving/money";
import { csvName } from "@/lib/giving/report";
import type { MonthlyReport } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { colomboParts } from "@/lib/services/time";
import { cn, focusRing } from "@/lib/utils";
import { saveBlob } from "./saveBlob";
import { useGiftText } from "./GiftText";
import { useGivingError } from "./useGivingError";

/**
 * The institution's month for its committee (plan 09, M16, M17): bookings, how much each meal slot was
 * used, money and items received, voided gifts apart from every total, and its needs. The file and the
 * printed page carry the same figures; the shell and the buttons do not print.
 */
export function ReportScreen() {
  const t = useTranslations("Giving.report");
  const tStatus = useTranslations("Booking.status");
  const locale = useLocale();
  const dates = useDates();
  const errorMessage = useGivingError();
  const { money, gift } = useGiftText();
  const { can } = usePermissions();

  const thisMonth = useMemo(() => monthOf(colomboParts(new Date().toISOString()).date), []);
  const [month, setMonth] = useState(thisMonth);
  const [report, setReport] = useState<MonthlyReport | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async (which: string) => {
    setFailure(null);
    const result = await getMonthlyReport(which);
    if (!result.ok) return setFailure(result.failure);
    setReport(result.value);
  }, []);

  useEffect(() => {
    // Loading the month is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(month);
  }, [load, month]);

  async function saveCsv() {
    setSaving(true);
    setFailure(null);
    const result = await downloadReportCsv(month, locale);
    setSaving(false);
    if (!result.ok) return setFailure(result.failure);
    saveBlob(result.value, csvName("report", month));
  }

  if (failure && !report) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load(month)} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!report) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const bookings = report.bookings.reduce((sum, row) => sum + row.count, 0);

  return (
    <div className="grid gap-6 print:gap-4">
      <FormError>{failure && errorMessage(failure)}</FormError>

      <div className="flex flex-wrap items-end justify-between gap-3 print:hidden">
        <div className="flex items-center gap-2" role="group" aria-label={t("month")}>
          <Button variant="outline" onClick={() => setMonth(shiftMonth(month, -1))} aria-label={t("previousMonth")}>
            ‹
          </Button>
          <p className="min-w-40 text-center text-xl font-semibold text-brand" aria-live="polite">
            {dates.monthYear(`${month}-01`)}
          </p>
          <Button variant="outline" onClick={() => setMonth(shiftMonth(month, 1))} disabled={month >= thisMonth} aria-label={t("nextMonth")}>
            ›
          </Button>
        </div>
        <div className="flex flex-wrap gap-3">
          {can("GIVING.REPORTS.EXPORT") && (
            <Button variant="outline" onClick={() => void saveCsv()} disabled={saving}>
              {t("csv")}
            </Button>
          )}
          {can("GIVING.REPORTS.PRINT") && <Button onClick={() => window.print()}>{t("print")}</Button>}
        </div>
      </div>

      <article aria-label={t("article")} className="grid gap-6 print:gap-4 print:text-black">
        <header className="hidden print:grid print:gap-1">
          <p className="text-xl font-semibold">{inLocale(locale, report.name, report.nameSi, report.nameTa)}</p>
          <p>{t("printTitle", { month: dates.monthYear(`${report.month}-01`) })}</p>
        </header>

        {!report.monthOver && (
          <p role="status" className="rounded-cup border border-hairline bg-subtle p-3 text-fg print:border-black print:bg-white print:text-black">
            {t("notOver")}
          </p>
        )}

        <div className="grid items-start gap-6 lg:grid-cols-2 print:block print:space-y-4">
          <Section title={t("bookings")}>
            <Rows>
              {report.bookings.map((row) => (
                <Row key={row.status} label={tStatus(row.status)} value={String(row.count)} />
              ))}
              <Row label={t("bookingsTotal")} value={String(bookings)} strong />
            </Rows>
          </Section>

          <Section title={t("utilisation")} note={t("utilisationNote")}>
            {report.utilisation.length === 0 ? (
              <Quiet>{t("noSlots")}</Quiet>
            ) : (
              <Rows>
                {report.utilisation.map((row) => (
                  <Row
                    key={row.mealSlot}
                    label={inLocale(locale, row.name, row.nameSi, row.nameTa)}
                    value={
                      row.servedDays === null
                        ? t("retired", { booked: row.bookedDays })
                        : row.percent === null
                          ? t("noDays")
                          : t("used", { booked: row.bookedDays, served: row.servedDays, percent: row.percent })
                    }
                  />
                ))}
              </Rows>
            )}
          </Section>

          <Section title={t("money")}>
            {report.money.length === 0 ? (
              <Quiet>{t("noMoney")}</Quiet>
            ) : (
              <Rows>
                {report.money.map((row) => (
                  <Row
                    key={row.code}
                    label={`${inLocale(locale, row.name, row.nameSi, row.nameTa)} · ${t("gifts", { count: row.gifts })}`}
                    value={money(row.amount)}
                  />
                ))}
                <Row label={t("moneyTotal")} value={money(report.moneyTotal)} strong />
              </Rows>
            )}
          </Section>

          <Section title={t("inKind")} note={t("inKindNote")}>
            {report.inKind.length === 0 ? (
              <Quiet>{t("noItems")}</Quiet>
            ) : (
              <Rows>
                {report.inKind.map((row) => (
                  <Row
                    key={row.code ?? "none"}
                    label={row.name === null ? t("noNeed") : inLocale(locale, row.name, row.nameSi, row.nameTa)}
                    value={
                      row.estimatedValue === null
                        ? t("gifts", { count: row.gifts })
                        : t("giftsWorth", { count: row.gifts, value: money(row.estimatedValue) })
                    }
                  />
                ))}
              </Rows>
            )}
          </Section>

          <Section title={t("needs")}>
            <Rows>
              <Row label={t("needsOpened")} value={String(report.needs.opened)} />
              <Row label={t("needsFulfilled")} value={String(report.needs.fulfilled)} />
              <Row label={report.monthOver ? t("needsStillOpenThen") : t("needsStillOpen")} value={String(report.needs.stillOpen)} />
            </Rows>
          </Section>
        </div>

        <Section title={t("voided")} note={t("voidedNote")}>
          {report.voided.length === 0 ? (
            <Quiet>{t("noVoided")}</Quiet>
          ) : (
            <ul className="grid gap-3 lg:grid-cols-2 print:block print:space-y-2">
              {report.voided.map((row) => (
                <li key={row.publicId} className="grid gap-1 rounded-cup border border-hairline p-3 print:break-inside-avoid print:border-black">
                  <p className="min-w-0 break-words font-semibold text-fg print:text-black">{gift(row)}</p>
                  <p className="min-w-0 break-words text-fg print:text-black">{row.donorName}</p>
                  <p className="text-sm text-muted print:text-black">
                    <Link href={`/receipts/${row.publicId}`} className={cn(textLink, focusRing, "min-h-0 tabular-nums print:text-black")}>
                      {row.receiptNo}
                    </Link>{" "}
                    · {dates.medium(colomboParts(row.donatedAt).date)}
                  </p>
                  <p className="min-w-0 break-words text-sm text-muted print:text-black">
                    {t("voidedBy", { name: row.voidedByName, date: dates.medium(colomboParts(row.voidedAt).date), reason: row.voidReason })}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Section>
      </article>
    </div>
  );
}

function Section({ title, note, children }: { title: string; note?: string; children: ReactNode }) {
  return (
    <section
      aria-label={title}
      className="grid min-w-0 gap-3 rounded-bowl border border-hairline bg-surface p-4 shadow-lift sm:p-5 print:break-inside-avoid print:rounded-none print:border-0 print:bg-white print:p-0 print:shadow-none"
    >
      <h2 className="text-lg font-semibold text-brand break-words print:text-black">{title}</h2>
      {note && <p className="text-sm text-muted print:text-black">{note}</p>}
      {children}
    </section>
  );
}

function Rows({ children }: { children: ReactNode }) {
  return <dl className="grid divide-y divide-hairline print:divide-black">{children}</dl>;
}

/** A label and its figure on a line; on a narrow phone the figure drops under a long label. */
function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={cn("flex flex-wrap items-baseline justify-between gap-x-4 py-2 first:pt-0 last:pb-0", strong && "font-semibold")}>
      <dt className="min-w-0 break-words text-fg print:text-black">{label}</dt>
      <dd className="min-w-0 break-words tabular-nums text-fg print:text-black">{value}</dd>
    </div>
  );
}

function Quiet({ children }: { children: ReactNode }) {
  return <p className="text-muted print:text-black">{children}</p>;
}
