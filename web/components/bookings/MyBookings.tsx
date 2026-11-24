"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import { cancelMyBooking, listMyBookings, listSharedWithMe, listYearly, skipMyRegularDay, type BookingFailure } from "@/lib/bookings/api";
import { asDate, colomboToday, daysBetween } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import type { DonorBooking, SharedBooking, YearlyBooking } from "@/lib/bookings/types";
import { listMyDayRequests } from "@/lib/sponsors/api";
import { canLetGoOwnDay } from "@/lib/sponsors/pattern";
import type { MyDayRequest } from "@/lib/sponsors/types";
import { cn, focusRing } from "@/lib/utils";
import { BookingStatusBadge } from "./BookingStatusBadge";
import { EmptyState } from "./EmptyState";
import { MyDayRequests } from "./MyDayRequests";
import { ShareSheet } from "./ShareSheet";
import { SharedWithMe } from "./SharedWithMe";
import { YearlyBookings } from "./YearlyBookings";
import { useBookingError } from "./useBookingError";
import { useWide } from "./useWide";
import { useDates } from "./useDates";

const MAX_REASON = 300;

/**
 * The donor's bookings (plan 03, 5.7): what is coming, soonest first, and what has been, latest
 * first. A booking says where and when as a date stub, what became of it, and why if it did not
 * go ahead. Cancelling is offered only while the donor may still do it (Q1). A regular day the
 * institution set up is not cancelled but let go ("I can't give that day", plan 08 K24): today or
 * later, by the institution's own rules, so it is recorded, never booked again, and the donor is texted.
 */
export function MyBookings() {
  const t = useTranslations("MyBookings");
  const [bookings, setBookings] = useState<DonorBooking[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [yearly, setYearly] = useState<YearlyBooking[]>([]);
  const [shared, setShared] = useState<SharedBooking[]>([]);
  const [requests, setRequests] = useState<MyDayRequest[]>([]);
  const [cancelling, setCancelling] = useState<DonorBooking | null>(null);
  const [skipping, setSkipping] = useState<DonorBooking | null>(null);
  const [sharing, setSharing] = useState<DonorBooking | null>(null);

  // The bookings decide the page; the yearly ones and those shared with the donor (plan 07) are
  // sections beside them, left out if they cannot be read.
  const load = useCallback(async () => {
    setFailed(false);
    const [result, yearlyResult, sharedResult, requestsResult] = await Promise.all([listMyBookings(), listYearly(), listSharedWithMe(), listMyDayRequests()]);
    if (yearlyResult.ok) setYearly(yearlyResult.value);
    if (requestsResult.ok) setRequests(requestsResult.value);
    if (sharedResult.ok) setShared(sharedResult.value);
    if (result.ok) setBookings(result.value);
    else setFailed(true);
  }, []);

  useEffect(() => {
    // Loading the list is what this component is for; the state it sets is the fetched data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

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

  if (bookings === null) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  if (bookings.length === 0 && yearly.length === 0 && shared.length === 0 && requests.length === 0) {
    return (
      <div className="grid gap-5">
        <EmptyState>{t("none")}</EmptyState>
        <Button asChild className="justify-self-center">
          <Link href="/find">{t("find")}</Link>
        </Button>
      </div>
    );
  }

  const today = colomboToday();
  const isUpcoming = (b: DonorBooking) => b.date >= today && (b.status === "Requested" || b.status === "Approved");
  const upcoming = bookings.filter(isUpcoming).sort((a, b) => a.date.localeCompare(b.date) || a.startTime.localeCompare(b.startTime));
  const past = bookings.filter((b) => !isUpcoming(b));

  return (
    <div className="grid gap-10">
      <Section title={t("upcoming")} empty={t("noneUpcoming")} bookings={upcoming} today={today} onCancel={setCancelling} onSkip={setSkipping} onShare={setSharing} />
      {requests.length > 0 && <MyDayRequests requests={requests} />}
      {yearly.length > 0 && <YearlyBookings yearly={yearly} onStopped={() => void load()} />}
      {shared.length > 0 && <SharedWithMe shared={shared} />}
      {past.length > 0 && <Section title={t("past")} bookings={past} today={today} onCancel={setCancelling} onSkip={setSkipping} onShare={setSharing} />}

      {sharing && <ShareSheet booking={sharing} onClose={() => setSharing(null)} />}

      {cancelling && (
        <CancelSheet
          booking={cancelling}
          onClose={() => setCancelling(null)}
          onCancelled={() => {
            setCancelling(null);
            void load();
          }}
        />
      )}

      {skipping && (
        <CancelSheet
          booking={skipping}
          skip
          onClose={() => setSkipping(null)}
          onCancelled={() => {
            setSkipping(null);
            void load();
          }}
        />
      )}
    </div>
  );
}

function Section({
  title,
  empty,
  bookings,
  today,
  onCancel,
  onSkip,
  onShare,
}: {
  title: string;
  empty?: string;
  bookings: DonorBooking[];
  today: string;
  onCancel: (booking: DonorBooking) => void;
  onSkip: (booking: DonorBooking) => void;
  onShare: (booking: DonorBooking) => void;
}) {
  return (
    <section className="grid gap-3">
      <h2 className="text-xl font-semibold text-brand">{title}</h2>
      {bookings.length === 0 ? (
        <p className="text-muted">{empty}</p>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {bookings.map((booking) => (
            <li key={booking.bookingNo}>
              <BookingCard
                booking={booking}
                today={today}
                onCancel={() => onCancel(booking)}
                onSkip={() => onSkip(booking)}
                onShare={() => onShare(booking)}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function BookingCard({
  booking,
  today,
  onCancel,
  onSkip,
  onShare,
}: {
  booking: DonorBooking;
  today: string;
  onCancel: () => void;
  onSkip: () => void;
  onShare: () => void;
}) {
  const t = useTranslations("MyBookings");
  const fulfilment = useTranslations("Booking.fulfilment");
  const dates = useDates();
  const locale = useLocale();
  const day = asDate(booking.date);
  const away = daysBetween(today, booking.date);
  const live = booking.status === "Requested" || booking.status === "Approved";
  // A regular day is let go rather than cancelled, today too, as the institution may (K24).
  const canSkip = canLetGoOwnDay(booking, today);
  const canCancel = booking.canCancel && !booking.regular;

  return (
    <article className="flex overflow-hidden rounded-bowl border border-hairline bg-surface shadow-lift">
      {/* The stub: the day, large, as on a temple's own dana calendar. */}
      <div
        className={cn(
          "grid w-20 shrink-0 content-center justify-items-center gap-0.5 border-r border-dashed border-hairline px-2 py-4 text-center",
          live ? "bg-brand/8 text-brand" : "bg-subtle text-muted",
        )}
      >
        <span className="text-sm font-medium">{dates.weekday(booking.date, "short")}</span>
        <span className="text-3xl font-semibold leading-none tabular-nums">{day.getUTCDate()}</span>
        <span className="text-sm">{dates.monthShort(booking.date)}</span>
      </div>

      <div className="grid min-w-0 flex-1 gap-1.5 p-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h3 className="min-w-0 wrap-anywhere text-lg font-semibold leading-snug text-fg">
            {inLocale(locale, booking.slotName, booking.slotNameSi, booking.slotNameTa)}
            <span className="font-normal text-muted"> · {booking.startTime}</span>
          </h3>
          <BookingStatusBadge status={booking.status} />
        </div>

        <Link href={`/i/${booking.institution}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1 wrap-anywhere")}>
          {inLocale(locale, booking.institutionName, booking.institutionNameSi, booking.institutionNameTa)}
        </Link>

        <p className="text-sm text-muted">
          {live && away >= 0 && away <= 7 && <span className="font-medium text-fg">{t("when", { days: away })} · </span>}
          {t("people", { count: booking.headcount })} · {fulfilment(booking.fulfilmentMode)} · {booking.bookingNo}
        </p>
        {booking.yearly && live && <p className="text-sm font-medium text-brand">{t("partOfYearly")}</p>}
        {booking.regular && <p className="text-sm font-medium text-brand">{t("regular")}</p>}

        {booking.inMemoryOf && <p className="wrap-anywhere font-tagline text-base italic text-fg">{t("inMemoryOf", { name: booking.inMemoryOf })}</p>}

        {booking.status === "Rejected" && booking.rejectReason && (
          <p className="wrap-anywhere rounded-cup bg-danger-soft px-3 py-2 text-sm text-fg">{t("rejectedBecause", { reason: booking.rejectReason })}</p>
        )}
        {booking.status === "Cancelled" && booking.cancelReason && (
          <p className="wrap-anywhere rounded-cup bg-subtle px-3 py-2 text-sm text-fg">{t("cancelledBecause", { reason: booking.cancelReason })}</p>
        )}
        {booking.status === "Requested" && <p className="text-sm text-muted">{t("waiting")}</p>}

        {/* Sharing needs a booking that is still to come (H36: Open). */}
        {((live && away >= 0) || canCancel) && (
          <div className="-ml-3 flex flex-wrap gap-1 pt-1">
            {live && away >= 0 && (
              <Button variant="ghost" onClick={onShare}>
                {t("shareThis")}
              </Button>
            )}
            {canCancel && (
              <Button variant="ghost" onClick={onCancel} className="text-danger hover:bg-danger/10">
                {t("cancel")}
              </Button>
            )}
            {canSkip && (
              <Button variant="ghost" onClick={onSkip} className="text-danger hover:bg-danger/10">
                {t("cantGive")}
              </Button>
            )}
          </div>
        )}
      </div>
    </article>
  );
}

/** Cancelling a booking, or with `skip` letting a regular day go: the same sheet, its own words and call. */
function CancelSheet({ booking, skip = false, onClose, onCancelled }: { booking: DonorBooking; skip?: boolean; onClose: () => void; onCancelled: () => void }) {
  const t = useTranslations(skip ? "MyBookings.skipSheet" : "MyBookings.cancelSheet");
  const toast = useToast();
  const wide = useWide();
  const errorMessage = useBookingError();
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setFailure(null);
    const result = await (skip ? skipMyRegularDay : cancelMyBooking)(booking.bookingNo, reason.trim() || null);
    setSubmitting(false);
    if (result.ok) {
      toast({ variant: "success", title: t("done", { bookingNo: booking.bookingNo }) });
      onCancelled();
    } else {
      setFailure(result.failure);
    }
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <form onSubmit={submit} className="contents">
          <SheetHeader>
            <SheetTitle>{t("title")}</SheetTitle>
            <SheetDescription>{t("description", { bookingNo: booking.bookingNo })}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-4">
            <Field label={t("reason")} description={t("reasonHint")}>
              <textarea
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                maxLength={MAX_REASON}
                rows={3}
                className={cn(inputClasses, "resize-y")}
              />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("keep")}
            </Button>
            <Button type="submit" variant="danger" loading={submitting}>
              {t("confirm")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
