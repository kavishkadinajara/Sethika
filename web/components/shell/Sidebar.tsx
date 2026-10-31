"use client";

import { useLocale, useTranslations } from "next-intl";
import { Tooltip } from "radix-ui";
import { useId, useSyncExternalStore, type ReactNode } from "react";
import { Bowl } from "@/components/bookings/Bowl";
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  SearchIcon,
} from "@/components/ui/icons";
import { Link } from "@/i18n/navigation";
import { localName } from "@/lib/bookings/names";
import type { TableSlot, TodaysTable } from "@/lib/bookings/types";
import {
  badgeCount,
  isActive,
  type NavIcon,
  type NavSection,
} from "@/lib/shell/nav";
import { cn, focusRing } from "@/lib/utils";
import { ContextCard } from "./ContextCard";
import { navIcons } from "./nav-icons";
import type { ShellData } from "./useShellData";

type SidebarProps = {
  sections: NavSection[];
  pathname: string;
  data: ShellData;
  /** The menu of the context before a switch, kept while the new one's permissions load. */
  busy: boolean;
  onJump: () => void;
};

/**
 * The living sidebar (plan 05, R2), from `lg` up: who the session acts as, jump to, the whole menu
 * with its counts, and today's table. It folds to a column of icons, a choice kept per browser.
 * Below `lg` MobileNav shows the same body in a sheet.
 */
export function Sidebar(props: SidebarProps) {
  const t = useTranslations("Shell");
  const [folded, setFolded] = useFolded();

  return (
    // The fold button's hint is outside the body, so the provider wraps the whole column.
    <Tooltip.Provider delayDuration={150} skipDelayDuration={300}>
      <aside
        aria-label={t("sidebar")}
        className={cn(
          // Below the top bar (z-30). Tooltips and menus are portalled, so nothing of the sidebar has
          // to climb out of its own stacking context or scroll box (the plan 04 rail's tooltip bug).
          "sticky top-16 hidden h-[calc(100dvh-4rem)] shrink-0 flex-col border-r border-hairline bg-surface lg:flex",
          "transition-[width] duration-200 motion-reduce:transition-none",
          folded ? "w-21" : "w-68",
        )}
      >
        <div className="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain px-3 py-4">
          <SidebarBody {...props} folded={folded} />
        </div>
        <div className="border-t border-hairline p-3">
          <Hint label={folded ? t("unfold") : null}>
            <button
              type="button"
              onClick={() => setFolded(!folded)}
              aria-expanded={!folded}
              aria-label={folded ? t("unfold") : undefined}
              className={cn(
                "flex min-h-11 w-full items-center gap-3 rounded-xl px-3 text-base text-muted transition-colors hover:bg-subtle hover:text-fg",
                folded && "justify-center px-0",
                focusRing,
              )}
            >
              {folded ? (
                <ChevronRightIcon className="size-5" />
              ) : (
                <ChevronLeftIcon className="size-5" />
              )}
              {!folded && t("fold")}
            </button>
          </Hint>
        </div>
      </aside>
    </Tooltip.Provider>
  );
}

/** The sidebar's contents, in the desktop column and in the phone's sheet alike. */
export function SidebarBody({
  sections,
  pathname,
  data,
  busy,
  onJump,
  folded = false,
  onNavigate,
}: SidebarProps & { folded?: boolean; onNavigate?: () => void }) {
  const t = useTranslations("Shell");

  return (
    <Tooltip.Provider delayDuration={150} skipDelayDuration={300}>
      <div className="flex flex-col gap-4">
        <ContextCard folded={folded} onSwitched={onNavigate} />

        <Hint label={folded ? t("jump.title") : null}>
          <button
            type="button"
            onClick={onJump}
            aria-keyshortcuts="Control+K Meta+K"
            aria-label={folded ? t("jump.title") : undefined}
            className={cn(
              "flex min-h-11 items-center gap-3 rounded-xl border border-hairline bg-canvas px-3 text-base text-muted",
              "transition-colors hover:border-line hover:text-fg",
              folded && "justify-center px-0",
              focusRing,
            )}
          >
            <SearchIcon className="size-5 shrink-0" />
            {!folded && (
              <>
                <span className="flex-1 text-left">{t("jump.title")}</span>
                <kbd className="hidden rounded-md border border-hairline px-1.5 font-sans text-xs lg:inline">
                  {t("jump.shortcut")}
                </kbd>
              </>
            )}
          </button>
        </Hint>

        <nav
          aria-label={t("mainMenu")}
          aria-busy={busy || undefined}
          className={cn("grid gap-4 transition-opacity", busy && "opacity-60")}
        >
          {sections.map((section) =>
            section.href !== undefined ? (
              <ul key={section.key} className="grid gap-1">
                <li>
                  <SideLink
                    href={section.href}
                    icon={section.icon}
                    label={t(`sections.${section.key}`)}
                    active={isActive(section.href, pathname)}
                    count={badgeCount(data.summary, section.badge)}
                    folded={folded}
                    onNavigate={onNavigate}
                  />
                </li>
              </ul>
            ) : (
              section.groups.map((group) => (
                <Group
                  key={`${section.key}.${group.key}`}
                  label={t(`groups.${group.key}`)}
                  folded={folded}
                >
                  {group.items.map((item) => (
                    <li key={item.key}>
                      <SideLink
                        href={item.href}
                        icon={item.icon}
                        label={t(`items.${item.key}`)}
                        active={isActive(item.href, pathname)}
                        count={badgeCount(data.summary, item.badge)}
                        folded={folded}
                        onNavigate={onNavigate}
                      />
                    </li>
                  ))}
                </Group>
              ))
            ),
          )}
        </nav>

        {data.today && !folded && (
          <TodayTable today={data.today} onNavigate={onNavigate} />
        )}
      </div>
    </Tooltip.Provider>
  );
}

/** A module's links under its name; folded, a hairline stands in for the name. */
function Group({
  label,
  folded,
  children,
}: {
  label: string;
  folded: boolean;
  children: ReactNode;
}) {
  const id = useId();

  return (
    <div className="grid gap-1">
      {folded ? (
        <span aria-hidden className="mx-auto mb-1 h-px w-8 bg-hairline" />
      ) : (
        <p
          id={id}
          className="px-3 text-xs font-semibold tracking-wide text-muted uppercase"
        >
          {label}
        </p>
      )}
      <ul
        aria-labelledby={folded ? undefined : id}
        aria-label={folded ? label : undefined}
        className="grid gap-1"
      >
        {children}
      </ul>
    </div>
  );
}

function SideLink({
  href,
  icon,
  label,
  active,
  count,
  folded,
  onNavigate,
}: {
  href: string;
  icon: NavIcon;
  label: string;
  active: boolean;
  count: number | null;
  folded: boolean;
  onNavigate?: () => void;
}) {
  const t = useTranslations("Shell");
  const Icon = navIcons[icon];
  const waiting = count === null ? null : t("badge", { count });

  return (
    <Hint label={folded ? (waiting ? `${label} · ${waiting}` : label) : null}>
      <Link
        href={href}
        onClick={onNavigate}
        aria-current={active ? "page" : undefined}
        aria-label={
          folded ? (waiting ? `${label}, ${waiting}` : label) : undefined
        }
        className={cn(
          "relative flex min-h-11 items-center gap-3 rounded-xl px-3 py-2 text-base leading-snug transition-colors",
          folded && "justify-center px-0",
          focusRing,
          active
            ? "bg-primary/10 font-semibold text-brand before:absolute before:inset-y-2.5 before:left-0 before:w-[3px] before:rounded-r-full before:bg-accent"
            : "text-fg hover:bg-subtle hover:text-brand",
        )}
      >
        <Icon
          className={cn(
            "size-5 shrink-0",
            active ? "text-brand" : "text-muted",
          )}
        />
        {!folded && <span className="min-w-0 flex-1">{label}</span>}
        {count !== null &&
          (folded ? (
            <span
              aria-hidden
              className="absolute top-1.5 right-3 size-2.5 rounded-full bg-accent ring-2 ring-surface"
            />
          ) : (
            <span className="min-w-6 rounded-full bg-accent px-2 text-center text-sm font-semibold text-on-accent tabular-nums">
              <span aria-hidden>{count}</span>
              <span className="sr-only">{waiting}</span>
            </span>
          ))}
      </Link>
    </Hint>
  );
}

/**
 * Today's meals as bowls (plan 05, R2): each fills as sponsors take its places, and leads to the
 * calendar, which opens on today. Its words say what the bowl shows, for those who cannot see it.
 */
function TodayTable({
  today,
  onNavigate,
}: {
  today: TodaysTable;
  onNavigate?: () => void;
}) {
  const t = useTranslations("Shell.today");
  const id = useId();

  return (
    <section
      aria-labelledby={id}
      className="grid gap-2 rounded-2xl border border-hairline bg-canvas p-3"
    >
      <p
        id={id}
        className="px-1 text-xs font-semibold tracking-wide text-muted uppercase"
      >
        {t("title")}
      </p>
      {today.slots.length === 0 ? (
        <p className="px-1 text-sm text-muted">{t("none")}</p>
      ) : (
        <ul className="grid gap-1">
          {today.slots.map((slot) => (
            <li key={slot.mealSlot}>
              <TableRow slot={slot} onNavigate={onNavigate} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function TableRow({
  slot,
  onNavigate,
}: {
  slot: TableSlot;
  onNavigate?: () => void;
}) {
  const t = useTranslations("Shell.today");
  const locale = useLocale();
  const fill =
    slot.state === "Closed" ? 0 : slot.bookedSponsors / slot.maxSponsors;
  const state =
    slot.state === "Closed"
      ? t("closed")
      : slot.state === "Full"
        ? t("full")
        : t("places", { booked: slot.bookedSponsors, max: slot.maxSponsors });

  return (
    <Link
      href="/bookings/calendar"
      onClick={onNavigate}
      className={cn(
        "flex min-h-11 items-center gap-3 rounded-xl px-1.5 py-1.5 transition-colors hover:bg-subtle",
        focusRing,
      )}
    >
      <Bowl
        fill={fill}
        className={cn(
          "h-5 w-8 shrink-0",
          slot.state === "Closed"
            ? "text-muted opacity-60"
            : slot.state === "Full"
              ? "text-success"
              : "text-accent-strong",
        )}
      />
      <span className="grid min-w-0 flex-1">
        <span className="truncate text-sm font-medium text-fg">
          {localName(locale, slot)}
        </span>
        <span className="truncate text-xs text-muted">
          {slot.startTime} · {state}
        </span>
      </span>
      {slot.requested > 0 && (
        <span className="shrink-0 rounded-full bg-accent/15 px-2 text-xs font-semibold text-fg">
          {t("requested", { count: slot.requested })}
        </span>
      )}
    </Link>
  );
}

/** A name beside a folded control, on hover and on keyboard focus; nothing when `label` is null. */
function Hint({
  label,
  children,
}: {
  label: string | null;
  children: ReactNode;
}) {
  if (label === null) return <>{children}</>;

  return (
    <Tooltip.Root>
      <Tooltip.Trigger asChild>{children}</Tooltip.Trigger>
      <Tooltip.Portal>
        <Tooltip.Content
          side="right"
          sideOffset={10}
          className="z-50 rounded-full bg-ink px-3 py-1.5 text-sm font-medium text-ivory shadow-lift data-[state=delayed-open]:animate-menu-in motion-reduce:animate-none"
        >
          {label}
        </Tooltip.Content>
      </Tooltip.Portal>
    </Tooltip.Root>
  );
}

// ---- the fold, kept per browser ------------------------------------------------------------------

const FOLD_KEY = "sethika.sidebar.folded";
const foldListeners = new Set<() => void>();

function readFolded(): boolean {
  try {
    return window.localStorage.getItem(FOLD_KEY) === "1";
  } catch {
    return false;
  }
}

function subscribeFolded(listener: () => void) {
  foldListeners.add(listener);
  window.addEventListener("storage", listener);
  return () => {
    foldListeners.delete(listener);
    window.removeEventListener("storage", listener);
  };
}

/** Open by default and on the server; storage that throws (private mode, blocked) just forgets. */
function useFolded(): [boolean, (folded: boolean) => void] {
  const folded = useSyncExternalStore(subscribeFolded, readFolded, () => false);

  function setFolded(next: boolean) {
    try {
      window.localStorage.setItem(FOLD_KEY, next ? "1" : "0");
    } catch {
      // Kept for this page only.
    }
    foldListeners.forEach((listener) => listener());
  }

  return [folded, setFolded];
}
