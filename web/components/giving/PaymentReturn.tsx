"use client";

import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import { getMyPayment } from "@/lib/giving/api";
import { formatAmount } from "@/lib/giving/money";
import { RETURN_CHECK_MS, RETURN_CHECKS, returnState } from "@/lib/giving/payments";
import type { MyPayment } from "@/lib/giving/types";
import { useGivingError } from "./useGivingError";

/**
 * Where PayHere sends the donor back (plan 09, M9). The address proves nothing, and PayHere passes no
 * status on it, so this page shows only what the API knows from the signed notification. That can arrive
 * a moment after the donor does, so a pending order is asked about a few more times.
 */
export function PaymentReturn({ orderId }: { orderId: string }) {
  const t = useTranslations("Giving.return");
  const giving = useTranslations("Giving");
  const locale = useLocale();
  const errorMessage = useGivingError();
  const left = useSearchParams().get("left") === "1";
  const [payment, setPayment] = useState<MyPayment | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [checking, setChecking] = useState(false);
  const checks = useRef(0);

  const load = useCallback(async () => {
    const result = await getMyPayment(orderId);
    if (!result.ok) return setFailure(result.failure);
    setFailure(null);
    setPayment(result.value);
  }, [orderId]);

  useEffect(() => {
    // Loading the order is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const waiting = payment !== null && returnState(payment.status, left) === "waiting";

  useEffect(() => {
    if (!waiting || checks.current >= RETURN_CHECKS) return;
    const timer = setTimeout(() => {
      checks.current++;
      void load();
    }, RETURN_CHECK_MS);
    return () => clearTimeout(timer);
  }, [waiting, payment, load]);

  if (failure) return <FormError>{errorMessage(failure)}</FormError>;

  if (!payment) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const name = inLocale(locale, payment.institutionName, payment.institutionNameSi, payment.institutionNameTa);
  const state = returnState(payment.status, left);
  const back = (
    <Button asChild variant="outline">
      <Link href={`/i/${payment.institutionSlug}`}>{t("backToPlace", { name })}</Link>
    </Button>
  );

  return (
    <Card accent className="grid max-w-2xl gap-3 p-4 sm:p-6" data-testid="payment-return" data-state={state}>
      {state === "paid" && (
        <>
          <h2 className="text-xl font-semibold text-brand">{t("paidTitle")}</h2>
          <p className="text-fg">{t("paidBody", { name, money: giving("money", { amount: formatAmount(payment.amount) }) })}</p>
          <div className="flex flex-wrap gap-3 pt-1">
            {payment.receiptPublicId && (
              <Button asChild>
                <Link href={`/receipts/${payment.receiptPublicId}`}>{t("receipt", { receiptNo: payment.receiptNo ?? "" })}</Link>
              </Button>
            )}
            <Button asChild variant="outline">
              <Link href="/giving/mine">{t("mine")}</Link>
            </Button>
          </div>
        </>
      )}

      {state === "waiting" && (
        <>
          <h2 className="text-xl font-semibold text-brand">{t("waitingTitle")}</h2>
          <p className="text-fg">{t("waitingBody")}</p>
          <div className="flex flex-wrap gap-3 pt-1">
            <Button
              variant="outline"
              loading={checking}
              onClick={async () => {
                setChecking(true);
                await load();
                setChecking(false);
              }}
            >
              {t("checkAgain")}
            </Button>
          </div>
        </>
      )}

      {state === "failed" && (
        <>
          <h2 className="text-xl font-semibold text-brand">{t("failedTitle")}</h2>
          <p className="text-fg">{t("failedBody", { name })}</p>
          <div className="pt-1">{back}</div>
        </>
      )}

      {state === "left" && (
        <>
          <h2 className="text-xl font-semibold text-brand">{t("leftTitle")}</h2>
          <p className="text-fg">{t("leftBody", { name })}</p>
          <div className="pt-1">{back}</div>
        </>
      )}
    </Card>
  );
}
