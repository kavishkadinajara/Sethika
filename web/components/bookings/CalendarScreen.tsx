"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { decide, getCalendar, type BookingFailure } from "@/lib/bookings/api";
import { addDays, colomboToday, firstOfMonth, lastOfMonth, monthOf } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import type { CalendarBooking, Decision, SlotDay } from "@/lib/bookings/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { BookingStatusBadge } from "./BookingStatusBadge";
import { Bowl } from "./Bowl";
import { BowlCalendar } from "./BowlCalendar";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";

/** How far ahead the "to decide" tray looks: the API's widest read (§8, E19). */
const TRAY_DAYS = 62;
const MAX_REASON = 300;

/**
 * The institution's calendar (plan 03, 5.7). Requests waiting for an answer come first, in a tray
 * that can be cleared from where it is (Q4: the waiting count is the institution's signal, not a
 * text). Under it, the month: each booked day a bowl as full as its places, a gold dot where a
 * request waits, and the chosen day's bookings with what can be done to each.
 */
export function CalendarScreen() {
  const t = useTranslations("Calendar");
  const errorMessage = useBookingError();
  const dates = useDates();
  const today = colomboToday();

  const [month, setMonth] = useState(monthOf(today));
  const [selected, setSelected] = useState<string | null>(today);
  const [days, setDays] = useState<SlotDay[] | null>(null);
  const [waiting, setWaiting] = useState<SlotDay[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const loadMonth = useCallback(async (wanted: string) => {
    setDays(null);
    const result = await getCalendar(firstOfMonth(wanted), lastOfMonth(wanted));
    if (result.ok) setDays(result.value);
    else setFailure(result.failure);
  }, []);

  const loadWaiting = useCallback(async () => {
    const result = await getCalendar(today, addDays(today, TRAY_DAYS - 1), "Requested");
    if (result.ok) setWaiting(result.value);
    else setFailure(result.failure);
  }, [today]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadMonth(month);
  }, [month, loadMonth]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadWaiting();
  }, [loadWaiting]);

  /** After any decision: both views may have changed. */
  const reload = () => {
    void loadWaiting();
    void loadMonth(month);
  };

  const onDate = (date: string) => days?.filter((d) => d.date === date) ?? [];
  const selectedDays = selected ? onDate(selected) : [];

  return (
    <div className="grid gap-8">
      {failure && (
        <FormError>
          {errorMessage(failure)}{" "}
          <button
            type="button"
            onClick={() => {
              setFailure(null);
              reload();
            }}
            className={cn(textLink, focusRing, "min-h-0")}
          >
            {t("retry")}
          </button>
        </FormError>
      )}

      <Tray days={waiting} onDecided={reload} />

      <div className="grid items-start gap-8 lg:grid-cols-[minmax(0,1fr)_24rem]">
        <Card className="p-4 sm:p-6">
          <BowlCalendar
            month={month}
            onMonthChange={(next) => {
              setMonth(next);
              setSelected(null);
            }}
            canGoBack
            canGoForward
            selected={selected}
            onSelect={setSelected}
            loading={days === null}
            day={(date) => dayView(date)}
          >
            <p className="flex items-center gap-2 text-sm text-muted">
              <span aria-hidden className="size-2.5 rounded-full bg-accent" /> {t("waitingDot")}
            </p>
          </BowlCalendar>
        </Card>

        <section aria-live="polite" className="grid gap-4">
          {selected && <DayPanel date={selected} days={selectedDays} onDecided={reload} />}
        </section>
      </div>
    </div>
  );

  function dayView(date: string) {
    const booked = onDate(date);
    const long = dates.full(date);
    if (booked.length === 0) return { label: `${long}. ${t("nothingBooked")}`, disabled: false };

    const taken = booked.reduce((sum, d) => sum + d.bookedSponsors, 0);
    const places = booked.reduce((sum, d) => sum + d.maxSponsors, 0);
    const requests = booked.flatMap((d) => d.bookings).filter((b) => b.status === "Requested").length;

    return {
      content: (
        <span className="relative">
          <Bowl fill={places ? taken / places : 0} className="text-brand" />
          {requests > 0 && <span className="absolute -top-0.5 -right-1.5 size-2.5 rounded-full bg-accent ring-2 ring-surface" />}
        </span>
      ),
      label: `${long}. ${t("dayLabel", { taken, places })}${requests > 0 ? ` ${t("waitingLabel", { count: requests })}` : ""}`,
      disabled: false,
    };
  }
}

/** Requests waiting for an answer in the next two months, oldest day first. */
function Tray({ days, onDecided }: { days: SlotDay[] | null; onDecided: () => void }) {
  const t = useTranslations("Calendar.tray");

  if (days === null) {
    return (
      <div className="flex justify-center py-6 text-brand">
        <Spinner />
      </div>
    );
  }

  const requests = days.flatMap((day) => day.bookings.filter((b) => b.status === "Requested").map((booking) => ({ day, booking })));

  return (
    <section className="grid gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold text-brand">{t("heading")}</h2>
        <span className={cn("rounded-full px-3 py-0.5 text-sm font-semibold tabular-nums", requests.length ? "bg-accent text-on-accent" : "bg-subtle text-muted")}>
          {requests.length}
        </span>
      </div>
      {requests.length === 0 ? (
        <p className="font-tagline text-lg italic text-muted">{t("clear")}</p>
      ) : (
        <ul className="grid gap-3 md:grid-cols-2">
          {requests.map(({ day, booking }) => (
            <li key={booking.bookingNo}>
              <BookingItem day={day} booking={booking} showWhen onDecided={onDecided} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function DayPanel({ date, days, onDecided }: { date: string; days: SlotDay[]; onDecided: () => void }) {
  const t = useTranslations("Calendar");
  const locale = useLocale();
  const dates = useDates();

  return (
    <>
      <h2 className="text-xl font-semibold text-brand">{dates.full(date)}</h2>
      {days.length === 0 ? (
        <p className="text-muted">{t("nothingBooked")}</p>
      ) : (
        days.map((day) => (
          <div key={day.mealSlot} className="grid gap-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h3 className="text-lg font-semibold text-fg">
                {inLocale(locale, day.slotName, day.slotNameSi, day.slotNameTa)} <span className="font-normal text-muted">{day.startTime}</span>
              </h3>
              <p className="text-sm tabular-nums text-muted">
                {t("places", { taken: day.bookedSponsors, max: day.maxSponsors })} · {t("people", { taken: day.bookedHeadcount, max: day.maxHeadcount })}
              </p>
            </div>
            <ul className="grid gap-3">
              {day.bookings.map((booking) => (
                <li key={booking.bookingNo}>
                  <BookingItem day={day} booking={booking} onDecided={onDecided} />
                </li>
              ))}
            </ul>
          </div>
        ))
      )}
    </>
  );
}

/** One booking, and the decisions its status and the caller's permissions allow. */
function BookingItem({ day, booking, showWhen = false, onDecided }: { day: SlotDay; booking: CalendarBooking; showWhen?: boolean; onDecided: () => void }) {
  const t = useTranslations("Calendar.booking");
  const fulfilment = useTranslations("Booking.fulfilment");
  const locale = useLocale();
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useBookingError();
  const { can } = usePermissions();
  const [busy, setBusy] = useState<Decision | null>(null);
  const [asking, setAsking] = useState<"reject" | "cancel" | null>(null);
  const [reason, setReason] = useState("");
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const today = colomboToday();
  const requested = booking.status === "Requested";
  const approved = booking.status === "Approved";

  async function run(decision: Decision, why?: string) {
    setBusy(decision);
    setFailure(null);
    const result = await decide(booking.bookingNo, decision, why);
    setBusy(null);
    if (result.ok) {
      toast({ variant: "success", title: t(`done.${decision}`, { bookingNo: booking.bookingNo }) });
      setAsking(null);
      onDecided();
    } else {
      setFailure(result.failure);
      if (result.failure.code === "stale" || result.failure.code === "invalid_transition") onDecided();
    }
  }

  function submitReason(event: FormEvent) {
    event.preventDefault();
    if (asking) void run(asking, reason.trim());
  }

  return (
    <article className="grid gap-2 rounded-cup border border-hairline bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="grid min-w-0 gap-0.5">
          {showWhen && (
            <p className="text-sm font-medium text-brand">
              {dates.dayMonth(day.date)} ·{" "}
              {inLocale(locale, day.slotName, day.slotNameSi, day.slotNameTa)} {day.startTime}
            </p>
          )}
          <p className="font-semibold text-fg">{booking.donorName}</p>
          <a href={`tel:${booking.donorPhone}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1 tabular-nums")}>
            {booking.donorPhone}
          </a>
        </div>
        <BookingStatusBadge status={booking.status} />
      </div>

      <p className="text-sm text-muted">
        {t("people", { count: booking.headcount })} · {fulfilment(booking.fulfilmentMode)} · {booking.bookingNo}
      </p>
      {booking.inMemoryOf && <p className="font-tagline italic text-fg">{t("inMemoryOf", { name: booking.inMemoryOf })}</p>}
      {booking.notes && <p className="rounded-cup bg-subtle px-3 py-2 text-sm text-fg">{booking.notes}</p>}
      {booking.rejectReason && <p className="text-sm text-muted">{t("rejectedBecause", { reason: booking.rejectReason })}</p>}
      {booking.cancelReason && <p className="text-sm text-muted">{t("cancelledBecause", { reason: booking.cancelReason })}</p>}

      {asking ? (
        <form onSubmit={submitReason} className="grid gap-3 pt-1">
          <Field label={asking === "reject" ? t("rejectReason") : t("cancelReason")} description={t("reasonHint")} required>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              maxLength={MAX_REASON}
              rows={2}
              autoFocus
              className={cn(inputClasses, "resize-y")}
            />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="danger" loading={busy === asking} disabled={!reason.trim()}>
              {asking === "reject" ? t("reject") : t("cancel")}
            </Button>
            <Button variant="ghost" onClick={() => setAsking(null)}>
              {t("back")}
            </Button>
          </div>
        </form>
      ) : (
        <div className="flex flex-wrap gap-2 pt-1">
          {requested && can("BOOKINGS.CALENDAR.APPROVE") && (
            <Button variant="outline" loading={busy === "approve"} onClick={() => void run("approve")}>
              {t("approve")}
            </Button>
          )}
          {requested && can("BOOKINGS.CALENDAR.REJECT") && (
            <Button variant="ghost" onClick={() => setAsking("reject")} className="text-danger hover:bg-danger/10">
              {t("reject")}
            </Button>
          )}
          {approved && day.date <= today && can("BOOKINGS.CALENDAR.EDIT") && (
            <>
              <Button variant="outline" loading={busy === "complete"} onClick={() => void run("complete")}>
                {t("complete")}
              </Button>
              <Button variant="ghost" loading={busy === "no-show"} onClick={() => void run("no-show")}>
                {t("noShow")}
              </Button>
            </>
          )}
          {(requested || approved) && can("BOOKINGS.CALENDAR.EDIT") && (
            <Button variant="ghost" onClick={() => setAsking("cancel")} className="text-danger hover:bg-danger/10">
              {t("cancel")}
            </Button>
          )}
        </div>
      )}
      <FormError>{failure && errorMessage(failure)}</FormError>
    </article>
  );
}
