"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { formatAmount, formatQuantity } from "@/lib/giving/money";
import type { DonationKind } from "@/lib/giving/types";

/** The methods with a name in the messages: the counter's and the gateway's (14_GivingLookups.sql). */
const methods = ["CASH", "BANK_TRANSFER", "CHEQUE", "CARD", "PAYHERE"] as const;
type Method = (typeof methods)[number];

type Gift = { kind: DonationKind; amount: number | null; method?: string | null; items: string | null; quantity: number | null };

/**
 * A gift in words, in the page's language: "LKR 5,000.00" (with "Cash" beside it as a separate line),
 * or the items as staff typed them with how many.
 */
export function useGiftText() {
  const t = useTranslations("Giving");

  const money = useCallback((amount: number) => t("money", { amount: formatAmount(amount) }), [t]);

  const method = useCallback(
    (code: string | null | undefined) => (code && (methods as readonly string[]).includes(code) ? t(`methods.${code as Method}`) : null),
    [t],
  );

  const gift = useCallback(
    (g: Gift) =>
      g.kind === "Money"
        ? money(g.amount ?? 0)
        : g.quantity !== null
          ? t("itemsCounted", { items: g.items ?? "", count: formatQuantity(g.quantity) })
          : (g.items ?? ""),
    [t, money],
  );

  return { money, method, gift };
}
