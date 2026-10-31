"use client";

import { useTranslations } from "next-intl";
import { useEffect, useId, useState, type KeyboardEvent } from "react";
import { Field } from "@/components/ui/field";
import { CloseIcon, InstitutionIcon } from "@/components/ui/icons";
import { Input } from "@/components/ui/input";
import { listInstitutions } from "@/lib/admin/api";
import type { InstitutionQuery } from "@/lib/admin/list-query";
import type { AdminInstitution } from "@/lib/admin/types";
import { cn, focusRing } from "@/lib/utils";

/*  The accounts list narrowed to one institution's people (plan 05, X4): too many institutions for
    chips or a select, so a combobox that searches the institutions list as one types. The address
    keeps the slug; the field shows the institution's name, looked up again after a reload.     */

/** How long typing must settle before the institutions are asked, as the list's own search. */
const SEARCH_DELAY_MS = 300;
/** As many as a glance takes in; typing more narrows it. */
const SHOWN = 8;

const byName = (search: string): InstitutionQuery => ({ search, status: null, type: null, district: null, sort: "name", page: 1 });

export function InstitutionFilter({ value, onChange }: { value: string | null; onChange: (slug: string | null) => void }) {
  const t = useTranslations("Admin.users");
  const listId = useId();

  // The names of the slugs seen, for the slug the address holds; one no list knows shows as itself.
  // What the field shows is derived from them unless one is typing, so nothing has to be kept in step.
  const [names, setNames] = useState<Record<string, string>>({});
  const [typed, setTyped] = useState<string | null>(null);
  const [found, setFound] = useState<AdminInstitution[] | null>(null);
  const [highlight, setHighlight] = useState(0);

  const known = value === null || value in names;
  useEffect(() => {
    if (value === null || known) return;
    void listInstitutions(byName(value)).then((result) => {
      const match = result.ok ? result.value.items.find((i) => i.slug === value) : undefined;
      setNames((seen) => ({ ...seen, [value]: match?.name ?? value }));
    });
  }, [value, known]);

  const term = (typed ?? "").trim();
  useEffect(() => {
    if (term === "") return;
    let cancelled = false;
    const timer = setTimeout(() => {
      void listInstitutions(byName(term)).then((result) => {
        if (cancelled) return;
        setFound(result.ok ? result.value.items.slice(0, SHOWN) : []);
        setHighlight(0);
      });
    }, SEARCH_DELAY_MS);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [term]);

  const listed = term !== "" && found !== null;
  const options = listed ? found : [];
  const current = options.length === 0 ? -1 : Math.min(highlight, options.length - 1);
  const shown = typed ?? (value === null ? "" : (names[value] ?? ""));

  /** Done with typing: the field shows what is chosen again. */
  function settle() {
    setTyped(null);
    setFound(null);
  }

  function pick(institution: AdminInstitution) {
    setNames((seen) => ({ ...seen, [institution.slug]: institution.name }));
    settle();
    if (institution.slug !== value) onChange(institution.slug);
  }

  function clear() {
    settle();
    onChange(null);
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      if (typed !== null) event.preventDefault();
      settle();
    } else if (options.length > 0 && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
      event.preventDefault();
      setHighlight((current + (event.key === "ArrowDown" ? 1 : -1) + options.length) % options.length);
    } else if (event.key === "Enter" && current >= 0) {
      event.preventDefault();
      pick(options[current]!);
    }
  }

  return (
    <div className="relative w-full min-w-0">
      <Field label={t("institution")} description={t("institutionHint")}>
        <Input
          type="text"
          role="combobox"
          autoComplete="off"
          aria-expanded={listed}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={current >= 0 ? `${listId}-${current}` : undefined}
          value={shown}
          onChange={(event) => setTyped(event.target.value)}
          onKeyDown={onKeyDown}
          onBlur={settle}
          className={cn(value !== null && "pr-12")}
        />
      </Field>

      {value !== null && (
        <button
          type="button"
          aria-label={t("institutionClear")}
          onClick={clear}
          className={cn("absolute right-1 bottom-1 grid size-10 place-items-center rounded-full text-muted hover:bg-subtle hover:text-fg", focusRing)}
        >
          <CloseIcon className="size-5" />
        </button>
      )}

      <ul
        id={listId}
        role="listbox"
        aria-label={t("institution")}
        hidden={!listed}
        className="absolute inset-x-0 top-full z-30 mt-1 grid max-h-80 gap-1 overflow-y-auto overscroll-contain rounded-2xl border border-hairline bg-surface p-1.5 shadow-lift"
      >
        {options.length === 0 ? (
          <li className="px-3 py-2.5 text-sm text-muted">{t("institutionNone")}</li>
        ) : (
          options.map((institution, index) => (
            <li
              key={institution.slug}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === current}
              // Before the input's blur, which would close the list first.
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => pick(institution)}
              onMouseEnter={() => setHighlight(index)}
              className={cn("flex min-h-11 cursor-pointer items-start gap-3 rounded-xl px-3 py-2", index === current && "bg-subtle")}
            >
              <InstitutionIcon className="mt-0.5 size-4 shrink-0 text-muted" />
              <span className="grid min-w-0">
                <span className="font-medium text-fg">{institution.name}</span>
                <span className="text-sm text-muted">{[institution.type, institution.district].filter(Boolean).join(" · ")}</span>
              </span>
            </li>
          ))
        )}
      </ul>
    </div>
  );
}
