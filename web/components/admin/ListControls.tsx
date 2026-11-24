"use client";

import { useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { ChevronLeftIcon, ChevronRightIcon } from "@/components/ui/icons";
import { Input, inputClasses } from "@/components/ui/input";
import { usePathname, useRouter } from "@/i18n/navigation";
import { pageCount, pageRange, PAGE_SIZE, toQueryString, type InstitutionQuery, type UserQuery } from "@/lib/admin/list-query";
import { cn, focusRing } from "@/lib/utils";

/*  The platform lists' controls (plan 05, X4): a search that waits for typing to settle, filters
    as chips, an order, and pages. The state lives in the address (lib/admin/list-query.ts), so
    each change writes the URL and the screen reads the list again from it.                     */

/** How long typing must settle before the list is asked again. */
const SEARCH_DELAY_MS = 300;

/**
 * A list's state from the address, and a way to change it. Changing anything but the page goes
 * back to the first page: the old page number means nothing in a different list. A filter, the
 * order or a page is a step the back button undoes; a search replaces the address instead, or
 * every settled pause in typing would be one more step back.
 */
export function useListQuery<Q extends UserQuery | InstitutionQuery>(
  parse: (params: URLSearchParams) => Q,
  defaultSort: Q["sort"],
): [Q, (change: Partial<Q>) => void] {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const text = params.toString();
  const query = useMemo(() => parse(new URLSearchParams(text)), [parse, text]);

  const set = useCallback(
    (change: Partial<Q>) => {
      const next = { ...query, ...change, page: "page" in change ? (change.page ?? 1) : 1 };
      const href = `${pathname}${toQueryString(next, defaultSort)}`;
      const typed = Object.keys(change).every((key) => key === "search");
      if (typed) router.replace(href, { scroll: false });
      else router.push(href, { scroll: false });
    },
    [query, router, pathname, defaultSort],
  );

  return [query, set];
}

/** The search box: typed freely, sent once typing settles; a back button's older search shows again. */
export function ListSearch({ label, hint, value, onSearch }: { label: string; hint: string; value: string; onSearch: (search: string) => void }) {
  const [typed, setTyped] = useState(value);
  const [shown, setShown] = useState(value);
  if (value !== shown) {
    setShown(value);
    setTyped(value);
  }

  useEffect(() => {
    if (typed.trim() === value) return;
    const timer = setTimeout(() => onSearch(typed.trim()), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [typed, value, onSearch]);

  return (
    <div className="w-full max-w-xl">
      <Field label={label} description={hint}>
        <Input type="search" value={typed} onChange={(event) => setTyped(event.target.value)} autoComplete="off" />
      </Field>
    </div>
  );
}

/** One filter as a row of chips, "All" first; the chosen one is pressed. */
export function FilterChips<V extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: V; label: string }[];
  value: V | null;
  onChange: (value: V | null) => void;
}) {
  const t = useTranslations("Admin.list");
  const id = useId();
  const chips: { value: V | null; label: string }[] = [{ value: null, label: t("all") }, ...options];

  return (
    <div role="group" aria-labelledby={id} className="grid gap-2">
      <span id={id} className="text-sm font-medium text-muted">
        {label}
      </span>
      <div className="flex flex-wrap gap-2">
        {chips.map((chip) => {
          const on = chip.value === value;
          return (
            <button
              key={chip.value ?? "all"}
              type="button"
              aria-pressed={on}
              onClick={() => onChange(chip.value)}
              className={cn(
                "min-h-11 rounded-full border px-4 text-sm font-medium transition-colors",
                on ? "border-primary bg-primary text-on-primary" : "border-hairline bg-surface text-fg hover:border-line",
                focusRing,
              )}
            >
              {chip.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/** A labelled native select: the order, or a filter with too many values for chips. */
export function ListSelect<V extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: V | ""; label: string }[];
  value: V | "";
  onChange: (value: V | "") => void;
}) {
  const id = useId();
  return (
    <div className="grid min-w-0 gap-2">
      <label htmlFor={id} className="text-sm font-medium text-muted">
        {label}
      </label>
      <select id={id} value={value} onChange={(event) => onChange(event.target.value as V | "")} className={cn(inputClasses, "min-w-0 pr-8")}>
        {options.map((option) => (
          <option key={option.value || "all"} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

/** "25-48 of 312" above the list, announced when it changes. */
export function ListCount({ page, shown, total }: { page: number; shown: number; total: number }) {
  const t = useTranslations("Admin.list");
  const range = pageRange(page, PAGE_SIZE, shown);
  return (
    <p role="status" className="text-sm text-muted tabular-nums">
      {range ? t("showing", { from: range.from, to: range.to, total }) : t("noneOf", { total })}
    </p>
  );
}

/** Previous and next under the list, with where it is; nothing at all for a list of one page. */
export function Pager({ page, total, onPage }: { page: number; total: number; onPage: (page: number) => void }) {
  const t = useTranslations("Admin.list");
  const pages = pageCount(total, PAGE_SIZE);
  if (pages <= 1 && page <= 1) return null;

  return (
    <nav aria-label={t("pages")} className="flex flex-wrap items-center justify-between gap-3">
      <Button variant="outline" disabled={page <= 1} onClick={() => onPage(Math.min(page - 1, pages))}>
        <ChevronLeftIcon className="size-5" />
        {t("previous")}
      </Button>
      <span className="text-sm text-muted tabular-nums">{t("page", { page, count: pages })}</span>
      <Button variant="outline" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        {t("next")}
        <ChevronRightIcon className="size-5" />
      </Button>
    </nav>
  );
}
