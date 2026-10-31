"use client";

import { useTranslations } from "next-intl";
import { Dialog } from "radix-ui";
import { useId, useState, type KeyboardEvent } from "react";
import { SearchIcon } from "@/components/ui/icons";
import { useRouter } from "@/i18n/navigation";
import { matchJump, type JumpTarget } from "@/lib/shell/nav";
import { cn } from "@/lib/utils";
import { navIcons } from "./nav-icons";

/**
 * Jump to (plan 05, R2): `Ctrl K` (`⌘K` on a Mac) or the sidebar's button opens it; typing narrows
 * the screens the user may view, the arrows move, Enter opens. The ARIA combobox pattern: focus
 * stays in the field and `aria-activedescendant` names the highlighted option, so a screen reader
 * hears each one as the arrows move. Radix Dialog traps focus, closes on Escape and gives focus
 * back. It offers only the menu's links, already filtered: hiding is UX, the API decides.
 */
export function JumpPalette({
  open,
  onOpenChange,
  targets,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  targets: JumpTarget[];
}) {
  const t = useTranslations("Shell.jump");

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-scrim backdrop-blur-[2px] data-[state=open]:animate-overlay-in motion-reduce:animate-none" />
        <Dialog.Content
          aria-describedby={undefined}
          className={cn(
            "fixed top-[12dvh] left-1/2 z-50 flex max-h-[76dvh] w-[min(36rem,calc(100vw-2rem))] -translate-x-1/2 flex-col",
            "overflow-hidden rounded-bowl border border-hairline bg-surface text-fg shadow-lift",
            "data-[state=open]:animate-menu-in motion-reduce:animate-none focus-visible:outline-none",
          )}
        >
          <Dialog.Title className="sr-only">{t("title")}</Dialog.Title>
          {/* Mounted afresh on each opening, so it always starts empty at the first option. */}
          {open && <Search targets={targets} onChosen={() => onOpenChange(false)} />}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function Search({ targets, onChosen }: { targets: JumpTarget[]; onChosen: () => void }) {
  const t = useTranslations("Shell.jump");
  const router = useRouter();
  const listId = useId();
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  const found = matchJump(targets, query);
  const current = found.length === 0 ? -1 : Math.min(highlight, found.length - 1);

  function go(target: JumpTarget) {
    onChosen();
    router.push(target.href);
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (found.length === 0) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setHighlight((current + step + found.length) % found.length);
    } else if (event.key === "Enter") {
      event.preventDefault();
      go(found[current]!);
    }
  }

  return (
    <>
      <div className="flex items-center gap-3 border-b border-hairline px-4">
        <SearchIcon className="size-5 shrink-0 text-muted" />
        {/* Radix focuses the first control on opening: this field. */}
        <input
          type="text"
          role="combobox"
          aria-label={t("title")}
          aria-expanded
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={current >= 0 ? `${listId}-${current}` : undefined}
          placeholder={t("placeholder")}
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setHighlight(0);
          }}
          onKeyDown={onKeyDown}
          className="min-h-14 w-full min-w-0 bg-transparent text-base text-fg placeholder:text-muted focus:outline-none"
        />
      </div>

      <ul id={listId} role="listbox" aria-label={t("results")} className="grid gap-1 overflow-y-auto overscroll-contain p-2">
        {found.map((target, index) => {
          const Icon = navIcons[target.icon];
          const selected = index === current;
          return (
            <li
              key={target.href}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={selected}
              onPointerMove={() => setHighlight(index)}
              onClick={() => go(target)}
              className={cn(
                "flex min-h-12 cursor-pointer items-center gap-3 rounded-xl px-3 py-2",
                selected ? "bg-subtle text-brand" : "text-fg",
              )}
            >
              <Icon className={cn("size-5 shrink-0", selected ? "text-brand" : "text-muted")} />
              <span className="min-w-0 flex-1 leading-snug">
                <span className="block font-medium">{target.label}</span>
                {target.group !== target.label && <span className="block text-sm text-muted">{target.group}</span>}
              </span>
            </li>
          );
        })}
      </ul>

      {/* One live region, always there, so each change of the list is announced. */}
      <p role="status" className={found.length === 0 ? "px-5 pt-2 pb-5 text-base text-muted" : "sr-only"}>
        {found.length === 0 ? t("nothing", { query: query.trim() }) : t("count", { count: found.length })}
      </p>

      <p aria-hidden className="hidden border-t border-hairline px-4 py-2.5 text-sm text-muted sm:block">
        {t("keys")}
      </p>
    </>
  );
}
