"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useMemo, useState } from "react";
import { textLink } from "@/components/auth/links";
import { Bowl } from "@/components/bookings/Bowl";
import { BookingItem } from "@/components/bookings/CalendarScreen";
import { useDates } from "@/components/bookings/useDates";
import { Link } from "@/i18n/navigation";
import { getCalendar, getLedger, getToday } from "@/lib/bookings/api";
import { addDays, colomboToday, firstOfMonth, lastOfMonth, monthGrid, monthOf } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import type { LedgerEntry, SlotDay, TableSlot, TodaysTable } from "@/lib/bookings/types";
import { colomboTime, institutionSentence } from "@/lib/dashboard/sentence";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { markShellStale, onShellStale } from "@/lib/shell/summary";
import { cn, focusRing } from "@/lib/utils";
import { colomboClock, DayHeader, Panel, PanelEmpty, PanelPlaceholder, useNow } from "./ledger-ui";
import { InvitationsCard } from "./InvitationsCard";

/** How far ahead "Waiting for you" looks: the calendar tray's own reach, the API's widest read. */
const WAITING_DAYS = 62;
/** How many requests the panel shows before sending the rest to the calendar. */
const WAITING_SHOWN = 4;

/** The timeline's day, 05:00 to 21:00 in Colombo: when the meals of every faith fall. */
const DAY_START = 5 * 60;
const DAY_END = 21 * 60;
const HOURS = [6, 9, 12, 15, 18, 21];

const minutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
const along = (time: string) => Math.min(1, Math.max(0, (minutes(time) - DAY_START) / (DAY_END - DAY_START)));

const calendarOn = (date: string, bookingNo?: string) =>
  `/bookings/calendar?date=${date}${bookingNo ? `&booking=${encodeURIComponent(bookingNo)}` : ""}`;

type Day = { table: TodaysTable | null; waiting: SlotDay[] | null; month: SlotDay[] | null; ledger: LedgerEntry[] | null };

/**
 * The institution's Day Ledger (plan 05, R4). Each panel is behind the permission of the screen it
 * summarises, and its query is not sent without it. A decision made here is the calendar's own
 * call; it marks the shell stale, and the dashboard reloads with the sidebar.
 */
export function InstitutionDay({ name }: { name: string }) {
  const t = useTranslations("Dashboard");
  const locale = useLocale();
  const { can } = usePermissions();
  const now = useNow();
  const today = colomboToday(now);
  const reads = can("BOOKINGS.CALENDAR.VIEW");
  const answers = reads && (can("BOOKINGS.CALENDAR.APPROVE") || can("BOOKINGS.CALENDAR.REJECT"));

  const [day, setDay] = useState<Day>({ table: null, waiting: null, month: null, ledger: null });
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    if (!reads) return;
    const [table, waiting, month, ledger] = await Promise.all([
      getToday(),
      answers ? getCalendar(today, addDays(today, WAITING_DAYS - 1), "Requested") : Promise.resolve(null),
      getCalendar(firstOfMonth(monthOf(today)), lastOfMonth(monthOf(today))),
      getLedger(),
    ]);
    // A failed read keeps what was there; the next refresh tries again.
    setDay((previous) => ({
      table: table.ok ? table.value : previous.table,
      waiting: waiting === null ? null : waiting.ok ? waiting.value : previous.waiting,
      month: month.ok ? month.value : previous.month,
      ledger: ledger.ok ? ledger.value : previous.ledger,
    }));
    setFailed(!table.ok || (waiting !== null && !waiting.ok) || !month.ok || !ledger.ok);
  }, [reads, answers, today]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
    return onShellStale(() => void load());
  }, [load]);

  const requests = useMemo(
    () => day.waiting?.flatMap((d) => d.bookings.filter((b) => b.status === "Requested").map((booking) => ({ day: d, booking }))) ?? null,
    [day.waiting],
  );

  const loaded = reads ? day.table !== null && (!answers || requests !== null) : true;
  const sentence = loaded
    ? institutionSentence({ table: reads ? day.table : null, waiting: answers ? (requests?.length ?? 0) : null, locale, now: colomboTime(now) })
    : null;

  return (
    <div className="grid gap-8">
      <DayHeader context={t("asInstitution", { name })} sentence={sentence} />
      <InvitationsCard />
      {failed && (
        <p role="status" className="text-sm text-muted">
          {t("panels.stale")}{" "}
          <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
            {t("panels.retry")}
          </button>
        </p>
      )}

      {reads && (
        <>
          <Panel title={t("panels.today")} link={{ href: calendarOn(today), label: t("panels.openDay") }}>
            {day.table ? <Timeline table={day.table} now={colomboTime(now)} /> : <PanelPlaceholder rows={2} />}
          </Panel>

          <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
            <div className="grid min-w-0 gap-6">
              {answers && (
                <Panel
                  title={t("panels.waiting")}
                  link={requests && requests.length > WAITING_SHOWN ? { href: "/bookings/calendar", label: t("panels.allWaiting", { count: requests.length }) } : undefined}
                >
                  {requests === null ? (
                    <PanelPlaceholder />
                  ) : requests.length === 0 ? (
                    <PanelEmpty>{t("panels.nothingWaiting")}</PanelEmpty>
                  ) : (
                    <ul className="grid grid-cols-1 gap-3 lg:grid-cols-2">
                      {requests.slice(0, WAITING_SHOWN).map(({ day: d, booking }) => (
                        <li key={booking.bookingNo}>
                          <BookingItem day={d} booking={booking} showWhen onDecided={markShellStale} />
                        </li>
                      ))}
                    </ul>
                  )}
                </Panel>
              )}
              <Panel title={t("panels.ledger")}>
                {day.ledger === null ? <PanelPlaceholder /> : <Ledger entries={day.ledger} today={today} />}
              </Panel>
            </div>
            <Panel title={t("panels.month")} link={{ href: "/bookings/calendar", label: t("panels.openCalendar") }}>
              {day.month === null ? <PanelPlaceholder rows={5} /> : <MonthBowls days={day.month} today={today} />}
            </Panel>
          </div>
        </>
      )}
    </div>
  );
}

/**
 * Today's table on a line from 05:00 to 21:00: a bowl at each meal's time, as full as its places,
 * and a gold needle at now. The line is a picture; the list under it says the same in words.
 */
function Timeline({ table, now }: { table: TodaysTable; now: string }) {
  const t = useTranslations("Shell.today");
  const tNow = useTranslations("Dashboard.timeline");
  const locale = useLocale();

  if (table.slots.length === 0) return <PanelEmpty>{t("none")}</PanelEmpty>;

  // Meals at the same time stack upward instead of hiding each other.
  const level = new Map<string, number>();
  const placed = table.slots.map((slot) => {
    const n = level.get(slot.startTime) ?? 0;
    level.set(slot.startTime, n + 1);
    return { slot, n };
  });
  const showNow = minutes(now) >= DAY_START && minutes(now) <= DAY_END;

  return (
    <div className="grid gap-5">
      <div aria-hidden className="relative mx-4 h-24">
        <div className="absolute inset-x-0 bottom-7 h-px bg-line" />
        {HOURS.map((hour) => (
          <span
            key={hour}
            style={{ left: `${(along(`${String(hour).padStart(2, "0")}:00`) * 100).toFixed(2)}%` }}
            className="absolute bottom-0 -translate-x-1/2 text-xs text-muted tabular-nums"
          >
            {String(hour).padStart(2, "0")}:00
          </span>
        ))}
        {placed.map(({ slot, n }) => (
          <span
            key={slot.mealSlot}
            style={{ left: `${(along(slot.startTime) * 100).toFixed(2)}%`, bottom: `${2.25 + n * 2}rem` }}
            className={cn("absolute -translate-x-1/2", slot.state === "Closed" ? "text-muted opacity-50" : slot.startTime <= now ? "text-muted" : "text-brand")}
          >
            <Bowl fill={slot.maxSponsors ? slot.bookedSponsors / slot.maxSponsors : 0} className="h-7 w-11" />
            {slot.requested > 0 && <span className="absolute -top-0.5 -right-1 size-2.5 rounded-full bg-accent ring-2 ring-surface" />}
          </span>
        ))}
        {showNow && (
          <span style={{ left: `${(along(now) * 100).toFixed(2)}%` }} className="absolute top-0 bottom-6 w-0.5 -translate-x-1/2 rounded-full bg-accent">
            <span className="absolute -top-1 left-1/2 -translate-x-1/2 -translate-y-full rounded-full bg-accent px-2 py-0.5 text-[0.65rem] font-semibold tracking-wide text-on-accent uppercase">
              {tNow("now")}
            </span>
          </span>
        )}
      </div>

      <ol className="grid grid-cols-1 gap-2 sm:grid-cols-2 2xl:grid-cols-3">
        {table.slots.map((slot) => (
          <li key={slot.mealSlot}>
            <Link
              href={calendarOn(table.date)}
              className={cn("flex min-h-12 items-center gap-3 rounded-cup border border-hairline px-3 py-2 hover:border-line", focusRing)}
            >
              <span className="w-12 shrink-0 font-semibold tabular-nums text-fg">{slot.startTime}</span>
              <span className="grid min-w-0 flex-1 leading-snug">
                <span className="truncate font-medium text-fg">{inLocale(locale, slot.name, slot.nameSi, slot.nameTa)}</span>
                <span className="text-sm text-muted">{stateLine(slot, t)}</span>
              </span>
            </Link>
          </li>
        ))}
      </ol>
    </div>
  );
}

/** The sidebar's own words for a meal's places (Shell.today), so both say it the same way. */
function stateLine(slot: TableSlot, t: ReturnType<typeof useTranslations<"Shell.today">>): string {
  if (slot.state === "Closed") return t("closed");
  const places = slot.state === "Full" ? t("full") : t("places", { booked: slot.bookedSponsors, max: slot.maxSponsors });
  return slot.requested > 0 ? `${places} · ${t("requested", { count: slot.requested })}` : places;
}

/** The month as small bowls, each day as full as its places; a gold dot where a request waits. */
function MonthBowls({ days, today }: { days: SlotDay[]; today: string }) {
  const t = useTranslations("Dashboard.month");
  const dates = useDates();
  const weeks = monthGrid(monthOf(today));

  const byDate = new Map<string, { taken: number; places: number; requests: number }>();
  for (const d of days) {
    const sum = byDate.get(d.date) ?? { taken: 0, places: 0, requests: 0 };
    sum.taken += d.bookedSponsors;
    sum.places += d.maxSponsors;
    sum.requests += d.bookings.filter((b) => b.status === "Requested").length;
    byDate.set(d.date, sum);
  }

  return (
    <div className="grid gap-3">
      <p className="text-base font-medium text-fg">{dates.monthYear(today)}</p>
      <table className="w-full table-fixed border-separate border-spacing-1">
        <thead>
          <tr>
            {weeks[0]!.map((cell) => (
              <th key={cell.date} scope="col" className="text-xs font-medium text-muted">
                <abbr title={dates.weekday(cell.date, "long")} className="no-underline">
                  {dates.weekday(cell.date, "narrow")}
                </abbr>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {weeks.map((week) => (
            <tr key={week[0]!.date}>
              {week.map((cell) => {
                if (!cell.inMonth) return <td key={cell.date} />;
                const sum = byDate.get(cell.date);
                const label = sum
                  ? `${dates.full(cell.date)}. ${t("day", { taken: sum.taken, places: sum.places })}${sum.requests ? ` ${t("waiting", { count: sum.requests })}` : ""}`
                  : `${dates.full(cell.date)}. ${t("nothing")}`;
                return (
                  <td key={cell.date} className="p-0">
                    <Link
                      href={calendarOn(cell.date)}
                      aria-label={label}
                      aria-current={cell.date === today ? "date" : undefined}
                      className={cn(
                        "relative grid min-h-12 place-items-center gap-0.5 rounded-xl py-1 hover:bg-subtle",
                        cell.date === today && "ring-2 ring-accent",
                        focusRing,
                      )}
                    >
                      <span className={cn("text-xs tabular-nums", cell.date < today ? "text-muted" : "text-fg")}>{Number(cell.date.slice(8))}</span>
                      <Bowl fill={sum && sum.places ? sum.taken / sum.places : 0} className={cn("h-3 w-5", sum ? "text-brand" : "text-line")} />
                      {sum && sum.requests > 0 && <span className="absolute top-1 right-1 size-2 rounded-full bg-accent" />}
                    </Link>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** The last ten changes of the institution's bookings, each a line of the ledger. */
function Ledger({ entries, today }: { entries: LedgerEntry[]; today: string }) {
  const t = useTranslations("Dashboard.ledger");
  const locale = useLocale();
  const dates = useDates();

  if (entries.length === 0) return <PanelEmpty>{t("none")}</PanelEmpty>;

  return (
    <ol className="grid grid-cols-1 divide-y divide-hairline">
      {entries.map((entry) => {
        const on = colomboToday(new Date(entry.at));
        return (
          <li key={`${entry.bookingNo}-${entry.status}-${entry.at}`} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-3 py-3 first:pt-0 last:pb-0">
            <span className="text-sm text-muted tabular-nums">
              {on === today ? colomboClock(entry.at) : dates.dayMonth(on)}
            </span>
            <span className="grid gap-1">
              <span className="text-fg">
                {t(`line.${entry.status}`, {
                  donor: entry.donorName,
                  meal: inLocale(locale, entry.slotName, entry.slotNameSi, entry.slotNameTa),
                  date: dates.dayMonth(entry.date),
                })}
              </span>
              {/* No status chip: the sentence names the change, and a chip would read as the booking's status now. */}
              <Link href={calendarOn(entry.date, entry.bookingNo)} className={cn(textLink, focusRing, "min-h-0 justify-self-start text-sm tabular-nums")}>
                {entry.bookingNo}
              </Link>
            </span>
          </li>
        );
      })}
    </ol>
  );
}
