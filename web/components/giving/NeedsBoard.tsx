"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useMemo, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { getSearchLookups, type BookingFailure } from "@/lib/bookings/api";
import { localName } from "@/lib/bookings/names";
import type { SearchLookups } from "@/lib/bookings/types";
import { listBoard } from "@/lib/giving/api";
import type { PublicNeed } from "@/lib/giving/types";
import { cn, focusRing } from "@/lib/utils";
import { NeedCards } from "./NeedCards";
import { useNeedLookups } from "./NeedText";
import { useGivingError } from "./useGivingError";

type Filter = { district: string; type: string; category: string };

/**
 * The public needs board (plan 09, M12): open needs of every listed institution, urgent first, then the
 * nearest deadline, narrowed by district, kind of place and category. Anyone may look; pledging asks for a
 * sign-in.
 */
export function NeedsBoard() {
  const t = useTranslations("Giving.board");
  const locale = useLocale();
  const errorMessage = useGivingError();
  const { lookups } = useNeedLookups();
  const [places, setPlaces] = useState<SearchLookups>({ types: [], districts: [] });
  const [filter, setFilter] = useState<Filter>({ district: "", type: "", category: "" });
  const [needs, setNeeds] = useState<PublicNeed[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const query = useMemo(() => {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(filter)) if (value) params.set(key, value);
    return params.toString();
  }, [filter]);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await listBoard(query);
    if (!result.ok) return setFailure(result.failure);
    setNeeds(result.value);
  }, [query]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void getSearchLookups().then((result) => result.ok && setPlaces(result.value));
  }, []);

  const select = (label: string, key: keyof Filter, options: { code: string; name: string; nameSi: string | null; nameTa: string | null }[]) => (
    <Field label={label}>
      <select value={filter[key]} onChange={(e) => setFilter({ ...filter, [key]: e.target.value })} className={cn(inputClasses, "w-full")}>
        <option value="">{t("all")}</option>
        {options.map((option) => (
          <option key={option.code} value={option.code}>
            {localName(locale, option)}
          </option>
        ))}
      </select>
    </Field>
  );

  return (
    <div className="grid gap-6">
      <div className="grid gap-4 sm:grid-cols-3" role="group" aria-label={t("filters")}>
        {select(t("district"), "district", places.districts)}
        {select(t("type"), "type", places.types)}
        {select(t("category"), "category", lookups.categories)}
      </div>

      {failure ? (
        <FormError>
          {errorMessage(failure)}{" "}
          <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
            {t("retry")}
          </button>
        </FormError>
      ) : !needs ? (
        <div className="flex justify-center py-16 text-brand">
          <Spinner />
        </div>
      ) : needs.length === 0 ? (
        <EmptyState>{query ? t("noMatch") : t("none")}</EmptyState>
      ) : (
        <NeedCards needs={needs} base="/needs" showPlace onChanged={() => void load()} />
      )}
    </div>
  );
}
