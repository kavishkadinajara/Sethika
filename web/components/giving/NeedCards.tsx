"use client";

import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import { inLocale } from "@/lib/bookings/names";
import { needLeft } from "@/lib/giving/needs";
import type { PublicNeed } from "@/lib/giving/types";
import { cn, focusRing } from "@/lib/utils";
import { GiveOnlineSheet } from "./GiveOnlineSheet";
import { NeedProgress, PriorityTag, useNeedLookups } from "./NeedText";
import { PledgeSheet } from "./PledgeSheet";

/**
 * Open needs as cards, each with its progress and "Pledge" (plan 09, M12, M13): the public board and an
 * institution's own page show the same cards. `?need=` in the address (a donor back from signing in)
 * opens the sheet again, and `?giveNeed=` the give sheet. `base` is the page's own address, for that return.
 * `givesOnline` adds "Give money": only an institution's own page knows it, so the board never shows it.
 */
export function NeedCards({
  needs,
  base,
  showPlace,
  givesOnline = false,
  onChanged,
}: {
  needs: PublicNeed[];
  base: string;
  showPlace: boolean;
  givesOnline?: boolean;
  onChanged: () => void;
}) {
  const t = useTranslations("Giving.board");
  const locale = useLocale();
  const dates = useDates();
  const toast = useToast();
  const search = useSearchParams();
  const { category, unit } = useNeedLookups();
  const give = useTranslations("Giving.give");
  const [pledging, setPledging] = useState<PublicNeed | null>(() => needs.find((n) => n.publicId === search.get("need")) ?? null);
  const [giving, setGiving] = useState<PublicNeed | null>(() => (givesOnline ? (needs.find((n) => n.publicId === search.get("giveNeed")) ?? null) : null));

  return (
    <>
      <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3" aria-label={t("list")}>
        {needs.map((need) => {
          const left = needLeft(need);
          return (
            <li key={need.publicId}>
              <article aria-label={need.title} className="grid h-full content-start gap-2 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <h3 className="min-w-0 break-words text-lg font-semibold leading-snug text-brand">{need.title}</h3>
                  {need.priority === "Urgent" && <PriorityTag priority={need.priority} />}
                </div>
                {showPlace && (
                  <p className="break-words text-fg">
                    <Link href={`/i/${need.institutionSlug}`} className={cn(textLink, focusRing, "min-h-0")}>
                      {inLocale(locale, need.institutionName, need.institutionNameSi, need.institutionNameTa)}
                    </Link>
                    <span className="text-muted"> · {need.city}, {inLocale(locale, need.district, need.districtSi, need.districtTa)}</span>
                  </p>
                )}
                <p className="text-sm text-muted">
                  {category(need.category)}
                  {need.deadline && <> · {t("deadline", { date: dates.medium(need.deadline) })}</>}
                </p>
                {need.description && <p className="break-words text-sm text-fg">{need.description}</p>}
                <NeedProgress need={need} unit={unit(need.unit)} />
                <div className="mt-1 flex flex-wrap items-center gap-3">
                  {left > 0 ? (
                    <Button variant="outline" onClick={() => setPledging(need)} aria-label={t("pledgeNamed", { need: need.title })}>
                      {t("pledge")}
                    </Button>
                  ) : (
                    <p className="text-sm font-medium text-fg">{t("allPledged")}</p>
                  )}
                  {givesOnline && (
                    <Button variant="outline" onClick={() => setGiving(need)} aria-label={give("openNeedNamed", { need: need.title })}>
                      {give("openNeed")}
                    </Button>
                  )}
                </div>
              </article>
            </li>
          );
        })}
      </ul>

      {giving && (
        <GiveOnlineSheet
          slug={giving.institutionSlug}
          place={inLocale(locale, giving.institutionName, giving.institutionNameSi, giving.institutionNameTa)}
          need={giving}
          back={`${base}${base.includes("?") ? "&" : "?"}giveNeed=${encodeURIComponent(giving.publicId)}`}
          onClose={() => setGiving(null)}
        />
      )}

      {pledging && (
        <PledgeSheet
          need={pledging}
          unit={unit(pledging.unit)}
          back={`${base}${base.includes("?") ? "&" : "?"}need=${encodeURIComponent(pledging.publicId)}`}
          onClose={() => setPledging(null)}
          onPledged={() => {
            setPledging(null);
            toast({ variant: "success", title: t("pledged") });
            onChanged();
          }}
        />
      )}
    </>
  );
}
