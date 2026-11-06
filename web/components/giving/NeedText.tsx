"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { localName } from "@/lib/bookings/names";
import { getNeedLookups } from "@/lib/giving/api";
import { formatQuantity } from "@/lib/giving/money";
import { needBar } from "@/lib/giving/needs";
import type { LookupItem, NeedCounts, NeedLookups, NeedPriority } from "@/lib/giving/types";
import { cn } from "@/lib/utils";

/** The category and unit names, in the page's language; a code until the lists arrive. */
export function useNeedLookups() {
  const locale = useLocale();
  const [lookups, setLookups] = useState<NeedLookups>({ categories: [], units: [] });

  useEffect(() => {
    void getNeedLookups().then((result) => result.ok && setLookups(result.value));
  }, []);

  const name = useCallback(
    (list: LookupItem[], code: string) => {
      const found = list.find((item) => item.code === code);
      return found ? localName(locale, found) : code;
    },
    [locale],
  );

  return {
    lookups,
    category: useCallback((code: string) => name(lookups.categories, code), [name, lookups]),
    unit: useCallback((code: string) => name(lookups.units, code), [name, lookups]),
  };
}

/**
 * A need's progress in a bar and in words (M15): what arrived in full colour, what is promised lighter
 * on top of it. The words carry the figures; the bar repeats them for the eye.
 */
export function NeedProgress({ need, unit }: { need: NeedCounts; unit: string }) {
  const t = useTranslations("Giving.progress");
  const { received, promised } = needBar(need);

  return (
    <div className="grid gap-1.5">
      <div
        role="progressbar"
        aria-label={t("label")}
        aria-valuemin={0}
        aria-valuemax={need.quantityNeeded}
        aria-valuenow={need.quantityReceived}
        className="flex h-2.5 overflow-hidden rounded-full bg-hairline"
      >
        <div className="bg-primary" style={{ width: `${received}%` }} />
        <div className="bg-primary/40" style={{ width: `${promised}%` }} />
      </div>
      <p className="text-sm text-muted" data-testid="need-progress">
        {t("figures", {
          received: formatQuantity(need.quantityReceived),
          pledged: formatQuantity(need.quantityPledged),
          needed: formatQuantity(need.quantityNeeded),
          unit,
        })}
      </p>
    </div>
  );
}

/** "Urgent" stands out; "Low" and "Normal" stay quiet. */
export function PriorityTag({ priority }: { priority: NeedPriority }) {
  const t = useTranslations("Giving.priority");

  return (
    <span
      className={cn(
        "rounded-full border px-3 py-1 text-sm font-semibold",
        priority === "Urgent" ? "border-danger/60 bg-danger-soft text-danger" : "border-hairline text-muted",
      )}
    >
      {t(priority)}
    </span>
  );
}
