"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { listBoard } from "@/lib/giving/api";
import type { PublicNeed } from "@/lib/giving/types";
import { NeedCards } from "./NeedCards";

/**
 * An institution's open needs on its own page (plan 09, M12). Quiet when it has none, like its services:
 * a page with no needs shows nothing rather than an empty heading.
 */
export function InstitutionNeeds({ slug }: { slug: string }) {
  const t = useTranslations("Giving.board");
  const [needs, setNeeds] = useState<PublicNeed[]>([]);

  const load = useCallback(async () => {
    const result = await listBoard(`institution=${encodeURIComponent(slug)}`);
    if (result.ok) setNeeds(result.value);
  }, [slug]);

  useEffect(() => {
    // Loading the needs is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (needs.length === 0) return null;

  return (
    <section className="grid gap-3" aria-labelledby="needs">
      <h2 id="needs" className="text-xl font-semibold text-brand">
        {t("onPage")}
      </h2>
      <p className="max-w-prose text-muted">{t("onPageIntro")}</p>
      <NeedCards needs={needs} base={`/i/${slug}`} showPlace={false} onChanged={() => void load()} />
    </section>
  );
}
