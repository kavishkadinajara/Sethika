"use client";

import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { inLocale } from "@/lib/bookings/names";
import type { PublicInstitution } from "@/lib/bookings/types";
import { GiveOnlineSheet } from "./GiveOnlineSheet";

/**
 * "Give online" on an institution's page (plan 09, M8, M10). Only an institution with an enabled merchant
 * shows it. `?give=1` in the address (a donor back from signing in) opens the sheet again.
 */
export function GiveOnline({ institution }: { institution: PublicInstitution }) {
  const t = useTranslations("Giving.give");
  const locale = useLocale();
  const search = useSearchParams();
  const [giving, setGiving] = useState(() => search.get("give") === "1");
  const place = inLocale(locale, institution.name, institution.nameSi, institution.nameTa);

  if (!institution.givesOnline) return null;

  return (
    <>
      <Card accent className="grid gap-3 p-4 sm:p-6">
        <h2 className="text-xl font-semibold text-brand">{t("heading")}</h2>
        <p className="max-w-prose text-muted">{t("intro", { name: place })}</p>
        <Button onClick={() => setGiving(true)} className="justify-self-start">
          {t("open")}
        </Button>
      </Card>

      {giving && <GiveOnlineSheet slug={institution.slug} place={place} back={`/i/${institution.slug}?give=1`} onClose={() => setGiving(false)} />}
    </>
  );
}
