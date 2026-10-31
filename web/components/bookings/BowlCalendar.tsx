"use client";

import { useTranslations } from "next-intl";
import { useRef, type KeyboardEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { ChevronLeftIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { addDays, asDate, firstOfMonth, monthGrid, monthOf } from "@/lib/bookings/calendar";
import { cn, focusRing } from "@/lib/utils";
import { useDates } from "./useDates";

export type DayView = {
  /** What the cell shows under the date. */
  content?: ReactNode;
  /** The whole day in words, for screen readers: the date, then what it holds. */
  label: string;
  /** Not offered, full, past: still focusable, so the reason can be heard, but not chosen. */
  disabled: boolean;
};

type BowlCalendarProps = {
  /** "yyyy-MM". */
  month: string;
  onMonthChange: (month: string) => void;
  canGoBack: boolean;
  canGoForward: boolean;
  selected: string | null;
  onSelect: (date: string) => void;
  /** Called for every day of the month, never for the neighbouring months' days. */
  day: (date: string) => DayView;
  /** The month's data is still coming: the grid shows, quiet, under a spinner. */
  loading?: boolean;
  /** The legend or a note, under the grid. */
  children?: ReactNode;
};

/**
 * A month as a grid of days, Sunday first - Sethika's own, not a calendar library (plan 03, §8):
 * every cell is a 44 px target, the grid runs edge to edge on a 320 px phone, and day and month
 * names come from Intl in the page's language.
 *
 * Keyboard: one day is in the tab order; the arrow keys move a day or a week, Home and End go to
 * the week's ends, Page Up and Page Down change the month.
 */
export function BowlCalendar({
  month,
  onMonthChange,
  canGoBack,
  canGoForward,
  selected,
  onSelect,
  day,
  loading = false,
  children,
}: BowlCalendarProps) {
  const t = useTranslations("Booking.calendar");
  const dates = useDates();
  const grid = useRef<HTMLDivElement>(null);
  const weeks = monthGrid(month);

  const title = dates.monthYear(firstOfMonth(month));
  // Any Sunday-to-Saturday week names the days; this one is in the grid already.
  const weekdays = weeks[0].map((cell) => ({
    short: dates.weekday(cell.date, "narrow"),
    long: dates.weekday(cell.date, "long"),
  }));

  const focusable = selected && monthOf(selected) === month ? selected : firstOfMonth(month);

  function focusDate(date: string) {
    if (monthOf(date) !== month) {
      const next = date > month ? canGoForward : canGoBack;
      if (!next) return;
      onMonthChange(monthOf(date));
      // The new month renders first; then the day can take focus.
      requestAnimationFrame(() => grid.current?.querySelector<HTMLElement>(`[data-date="${date}"]`)?.focus());
      return;
    }
    grid.current?.querySelector<HTMLElement>(`[data-date="${date}"]`)?.focus();
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const current = (event.target as HTMLElement).dataset.date;
    if (!current) return;
    const weekday = asDate(current).getUTCDay();
    const moves: Record<string, number> = {
      ArrowLeft: -1,
      ArrowRight: 1,
      ArrowUp: -7,
      ArrowDown: 7,
      Home: -weekday,
      End: 6 - weekday,
    };
    if (event.key in moves) {
      event.preventDefault();
      focusDate(addDays(current, moves[event.key]));
    } else if (event.key === "PageUp" || event.key === "PageDown") {
      event.preventDefault();
      if (event.key === "PageUp" ? canGoBack : canGoForward) {
        onMonthChange(monthOf(addDays(firstOfMonth(month), event.key === "PageUp" ? -1 : 32)));
      }
    }
  }

  return (
    <section aria-label={title} className="grid gap-3">
      <div className="flex items-center justify-between gap-2">
        <h3 aria-live="polite" className="text-lg font-semibold text-brand">
          {title}
        </h3>
        <div className="flex gap-1">
          <Button
            variant="ghost"
            size="icon"
            disabled={!canGoBack}
            onClick={() => onMonthChange(monthOf(addDays(firstOfMonth(month), -1)))}
          >
            <ChevronLeftIcon />
            <span className="sr-only">{t("previous")}</span>
          </Button>
          <Button
            variant="ghost"
            size="icon"
            disabled={!canGoForward}
            onClick={() => onMonthChange(monthOf(addDays(firstOfMonth(month), 32)))}
          >
            <ChevronLeftIcon className="rotate-180" />
            <span className="sr-only">{t("next")}</span>
          </Button>
        </div>
      </div>

      {/* Edge to edge on a phone: seven 44 px days need the whole 320 px. */}
      <div className="relative -mx-4 sm:mx-0">
        <div className="grid grid-cols-7 text-center text-sm font-medium text-muted" aria-hidden>
          {weekdays.map((weekday) => (
            <span key={weekday.long} title={weekday.long} className="py-1">
              {weekday.short}
            </span>
          ))}
        </div>

        <div ref={grid} onKeyDown={onKeyDown} className={cn("grid grid-cols-7 gap-y-1 transition-opacity", loading && "opacity-40")}>
          {weeks.flat().map((cell) => {
            if (!cell.inMonth) return <span key={cell.date} aria-hidden />;

            const view = day(cell.date);
            const isSelected = cell.date === selected;
            return (
              <button
                key={cell.date}
                type="button"
                data-date={cell.date}
                tabIndex={cell.date === focusable ? 0 : -1}
                aria-label={view.label}
                aria-pressed={isSelected}
                aria-disabled={view.disabled || undefined}
                onClick={() => !view.disabled && onSelect(cell.date)}
                className={cn(
                  "mx-auto flex min-h-14 w-full max-w-16 flex-col items-center justify-start gap-1 rounded-cup pt-1.5 pb-1",
                  "text-base tabular-nums transition-colors duration-150",
                  focusRing,
                  view.disabled
                    ? "cursor-default text-muted/60"
                    : "text-fg hover:bg-brand/8 active:scale-[0.97] motion-reduce:active:scale-100",
                  isSelected && "bg-brand/12 font-semibold text-brand ring-2 ring-brand",
                )}
              >
                <span aria-hidden>{asDate(cell.date).getUTCDate()}</span>
                <span aria-hidden className="flex min-h-5 items-center">
                  {view.content}
                </span>
              </button>
            );
          })}
        </div>

        {loading && (
          <div className="absolute inset-0 flex items-center justify-center text-brand">
            <Spinner />
          </div>
        )}
      </div>

      {children}
    </section>
  );
}
