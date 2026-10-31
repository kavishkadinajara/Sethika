"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import { listInstitutions } from "@/lib/bookings/api";
import { inLocale, localName } from "@/lib/bookings/names";
import type { PublicInstitutionSummary } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";
import { Bowl } from "./Bowl";

/**
 * The institutions that chose to be listed (plan 03, C10): a plain list, with a filter that looks
 * through every name it shows, in all three languages, so a donor can type in any of them.
 */
export function InstitutionDirectory() {
  const t = useTranslations("Discover");
  const locale = useLocale();
  const [institutions, setInstitutions] = useState<PublicInstitutionSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [filter, setFilter] = useState("");

  const load = useCallback(async () => {
    setFailed(false);
    const result = await listInstitutions();
    if (result.ok) setInstitutions(result.value);
    else setFailed(true);
  }, []);

  useEffect(() => {
    // Loading the list is what this component is for; the state it sets is the fetched data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const shown = useMemo(() => {
    const words = filter.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    if (!institutions || words.length === 0) return institutions;
    return institutions.filter((institution) => {
      const haystack = [
        institution.name, institution.nameSi, institution.nameTa,
        institution.typeName, institution.typeNameSi, institution.typeNameTa,
        institution.district, institution.districtSi, institution.districtTa,
        institution.city,
      ].filter(Boolean).join(" ").toLocaleLowerCase();
      return words.every((word) => haystack.includes(word));
    });
  }, [institutions, filter]);

  if (failed) {
    return (
      <FormError>
        {t("loadFailed")}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (shown === null || institutions === null) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <div className="grid gap-6">
      {institutions.length > 0 && (
        <div className="max-w-xl">
          <Field label={t("filter")} description={t("filterHint")}>
            <Input type="search" value={filter} onChange={(event) => setFilter(event.target.value)} autoComplete="off" />
          </Field>
        </div>
      )}

      {institutions.length === 0 ? (
        <EmptyState>{t("noneListed")}</EmptyState>
      ) : shown.length === 0 ? (
        <EmptyState>{t("noneMatch")}</EmptyState>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
          {shown.map((institution) => (
            <li key={institution.slug}>
              <Link
                href={`/i/${institution.slug}`}
                className={cn(
                  "group flex h-full flex-col gap-1 rounded-bowl border border-hairline bg-surface p-5 shadow-lift",
                  "transition-[border-color,translate] duration-150 hover:-translate-y-0.5 hover:border-brand/50",
                  "motion-reduce:hover:translate-y-0",
                  focusRing,
                )}
              >
                <span className="text-lg font-semibold leading-snug text-brand">{localName(locale, institution)}</span>
                <span className="text-sm text-muted">
                  {inLocale(locale, institution.typeName, institution.typeNameSi, institution.typeNameTa)}
                </span>
                <span className="mt-auto pt-3 text-sm text-fg">
                  {institution.city} · {inLocale(locale, institution.district, institution.districtSi, institution.districtTa)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** A quiet empty state: the bowl, waiting, and one line in Sethika's human voice. */
export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className="grid justify-items-center gap-3 rounded-bowl border border-dashed border-hairline px-6 py-10 text-center">
      <Bowl fill={0} className="h-8 w-12 text-brand/60" />
      <p className="max-w-sm font-tagline text-lg italic text-muted">{children}</p>
    </div>
  );
}
