"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type ReactNode } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { Notice } from "@/components/onboarding/ApplyGate";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Link, usePathname } from "@/i18n/navigation";
import { useSession } from "@/lib/auth/useSession";
import type { BookingFailure } from "@/lib/bookings/api";
import { localName } from "@/lib/bookings/names";
import { getReceipt } from "@/lib/giving/api";
import { formatAmount, formatQuantity } from "@/lib/giving/money";
import type { Receipt } from "@/lib/giving/types";
import { colomboParts } from "@/lib/services/time";
import { cn, focusRing } from "@/lib/utils";
import { useGiftText } from "./GiftText";
import { useGivingError } from "./useGivingError";

/**
 * The printable receipt (plan 09, M4): the browser prints it, or saves it as a PDF. The institution
 * reads it through its session, the donor through their own; the API decides which (and answers 404
 * for anyone else). A voided gift says VOID, why and when. It acknowledges a gift; it is not a tax
 * receipt.
 */
export function ReceiptScreen({ publicId }: { publicId: string }) {
  const t = useTranslations("Giving.receipt");
  const locale = useLocale();
  const dates = useDates();
  const session = useSession();
  const pathname = usePathname();
  const errorMessage = useGivingError();
  const { money, method } = useGiftText();
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const mine = session.status === "authenticated" && !session.activeInstitution;

  const load = useCallback(async () => {
    setFailure(null);
    const result = await getReceipt(publicId, mine);
    if (!result.ok) return setFailure(result.failure);
    setReceipt(result.value);
  }, [publicId, mine]);

  useEffect(() => {
    if (session.status !== "authenticated") return;
    // Loading the receipt is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load, session.status]);

  if (session.status === "loading") {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  if (session.status !== "authenticated") {
    return (
      <Notice>
        <p>{t("signIn")}</p>
        <p className="mt-2">
          <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={cn(textLink, focusRing)}>
            {t("signInLink")}
          </Link>
        </p>
      </Notice>
    );
  }

  if (failure) return <FormError>{errorMessage(failure)}</FormError>;

  if (!receipt) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const given = colomboParts(receipt.donatedAt).date;
  const recorded = colomboParts(receipt.recordedAt);
  const place = receipt.institution;

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button onClick={() => window.print()}>{t("print")}</Button>
        <Button asChild variant="ghost">
          <Link href={mine ? "/giving/mine" : "/giving/donations"}>{mine ? t("backMine") : t("backList")}</Link>
        </Button>
      </div>

      <article
        aria-label={t("label", { receiptNo: receipt.receiptNo })}
        className="relative grid gap-6 overflow-hidden rounded-bowl border border-hairline bg-surface p-6 shadow-lift print:rounded-none print:border-0 print:bg-white print:p-0 print:text-black print:shadow-none sm:p-8"
      >
        {receipt.isVoided && (
          <div role="status" className="rounded-cup border-2 border-danger bg-danger-soft p-4 text-danger print:border-black print:bg-white print:text-black">
            <p className="text-2xl font-bold tracking-widest">{t("void")}</p>
            <p className="mt-1 break-words">
              {t("voidedOn", { date: dates.medium(colomboParts(receipt.voidedAt!).date), name: receipt.voidedByName ?? "" })}
            </p>
            <p className="mt-1 whitespace-pre-line break-words">{t("voidReason", { reason: receipt.voidReason ?? "" })}</p>
          </div>
        )}

        <header className="grid gap-1 border-b border-hairline pb-4 print:border-black">
          <h2 className="text-2xl font-semibold text-brand print:text-black">{localName(locale, place)}</h2>
          <p className="text-muted print:text-black">
            {[place.addressLine1, place.addressLine2, place.city].filter(Boolean).join(", ")}
          </p>
          <p className="text-muted tabular-nums print:text-black">{place.phone}</p>
        </header>

        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <p className="text-xl font-semibold text-fg print:text-black">{t("title")}</p>
          <p className="text-lg font-semibold tabular-nums text-fg print:text-black">{receipt.receiptNo}</p>
        </div>

        <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-[minmax(0,12rem)_minmax(0,1fr)]">
          <Row term={t("receivedFrom")}>{receipt.donorName}</Row>
          {receipt.kind === "Money" ? (
            <>
              <Row term={t("amount")}>
                <span className="text-xl font-semibold tabular-nums">{money(receipt.amount ?? 0)}</span>
              </Row>
              {receipt.method && <Row term={t("method")}>{method(receipt.method)}</Row>}
            </>
          ) : (
            <>
              <Row term={t("items")}>
                <span className="whitespace-pre-line">{receipt.items}</span>
              </Row>
              {receipt.quantity !== null && <Row term={t("quantity")}>{formatQuantity(receipt.quantity)}</Row>}
              {receipt.estimatedValue !== null && <Row term={t("estimatedValue")}>{t("money", { amount: formatAmount(receipt.estimatedValue) })}</Row>}
            </>
          )}
          <Row term={t("given")}>{dates.medium(given)}</Row>
          {receipt.note && (
            <Row term={t("note")}>
              <span className="whitespace-pre-line">{receipt.note}</span>
            </Row>
          )}
          <Row term={t("recordedBy")}>{t("recordedOn", { name: receipt.recordedByName, date: dates.medium(recorded.date), time: recorded.time })}</Row>
        </dl>

        <p className="text-lg text-fg print:text-black">{t("thanks")}</p>
        <p className="border-t border-hairline pt-4 text-sm text-muted print:border-black print:text-black">{t("notTax")}</p>
      </article>
    </div>
  );
}

function Row({ term, children }: { term: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-sm font-medium text-muted print:text-black">{term}</dt>
      <dd className="min-w-0 break-words text-fg print:text-black">{children}</dd>
    </>
  );
}
