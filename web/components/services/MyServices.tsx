"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/InstitutionDirectory";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { asDate, colomboToday, daysBetween } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import { cancelMyAppointment, listMyAppointments } from "@/lib/services/api";
import { colomboParts } from "@/lib/services/time";
import type { DonorAppointment } from "@/lib/services/types";
import { cn, focusRing } from "@/lib/utils";
import { ServiceStatusBadge } from "./ServiceStatusBadge";
import { useServiceError } from "./useServiceError";

const MAX_REASON = 300;

type Shown = DonorAppointment & { date: string; startTime: string; endTime: string };

/**
 * The donor's religious-service requests (Phase E, 8.9): what is coming, soonest first, and what
 * has been, latest first, each as a date stub like a booking. Cancelling is offered only while the
 * donor may still do it, until the end of the day before (V13).
 */
export function MyServices() {
  const t = useTranslations("Services.mine");
  const [appointments, setAppointments] = useState<DonorAppointment[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [cancelling, setCancelling] = useState<Shown | null>(null);

  const load = useCallback(async () => {
    setFailed(false);
    const result = await listMyAppointments();
    if (result.ok) setAppointments(result.value);
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

  if (appointments === null) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  if (appointments.length === 0) {
    return (
      <div className="grid gap-5">
        <EmptyState>{t("none")}</EmptyState>
        <Button asChild className="justify-self-center">
          <Link href="/institutions">{t("find")}</Link>
        </Button>
      </div>
    );
  }

  const today = colomboToday();
  const shown: Shown[] = appointments.map((a) => {
    const start = colomboParts(a.startsAt);
    return { ...a, date: start.date, startTime: start.time, endTime: colomboParts(a.endsAt).time };
  });
  const isUpcoming = (a: Shown) => a.date >= today && (a.status === "Requested" || a.status === "Approved");
  const upcoming = shown.filter(isUpcoming).sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const past = shown.filter((a) => !isUpcoming(a));

  return (
    <div className="grid gap-10">
      <Section title={t("upcoming")} empty={t("noneUpcoming")} appointments={upcoming} today={today} onCancel={setCancelling} />
      {past.length > 0 && <Section title={t("past")} appointments={past} today={today} onCancel={setCancelling} />}

      {cancelling && (
        <CancelSheet
          appointment={cancelling}
          onClose={() => setCancelling(null)}
          onCancelled={() => {
            setCancelling(null);
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
  appointments,
  today,
  onCancel,
}: {
  title: string;
  empty?: string;
  appointments: Shown[];
  today: string;
  onCancel: (appointment: Shown) => void;
}) {
  return (
    <section className="grid gap-3">
      <h2 className="text-xl font-semibold text-brand">{title}</h2>
      {appointments.length === 0 ? (
        <p className="text-muted">{empty}</p>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {appointments.map((appointment) => (
            <li key={appointment.appointmentNo}>
              <AppointmentCard appointment={appointment} today={today} onCancel={() => onCancel(appointment)} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function AppointmentCard({ appointment, today, onCancel }: { appointment: Shown; today: string; onCancel: () => void }) {
  const t = useTranslations("Services.mine");
  const dates = useDates();
  const locale = useLocale();
  const away = daysBetween(today, appointment.date);
  const live = appointment.status === "Requested" || appointment.status === "Approved";

  return (
    <article className="flex overflow-hidden rounded-bowl border border-hairline bg-surface shadow-lift">
      <div
        className={cn(
          "grid w-20 shrink-0 content-center justify-items-center gap-0.5 border-r border-dashed border-hairline px-2 py-4 text-center",
          live ? "bg-brand/8 text-brand" : "bg-subtle text-muted",
        )}
      >
        <span className="text-sm font-medium">{dates.weekday(appointment.date, "short")}</span>
        <span className="text-3xl font-semibold leading-none tabular-nums">{asDate(appointment.date).getUTCDate()}</span>
        <span className="text-sm">{dates.monthShort(appointment.date)}</span>
      </div>

      <div className="grid min-w-0 flex-1 gap-1.5 p-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h3 className="min-w-0 text-lg font-semibold leading-snug text-fg">
            {inLocale(locale, appointment.serviceName, appointment.serviceNameSi, appointment.serviceNameTa)}
            <span className="font-normal text-muted">
              {" "}
              · {appointment.startTime}–{appointment.endTime}
            </span>
          </h3>
          <ServiceStatusBadge status={appointment.status} />
        </div>

        <Link href={`/i/${appointment.institution}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1")}>
          {inLocale(locale, appointment.institutionName, appointment.institutionNameSi, appointment.institutionNameTa)}
        </Link>

        <p className="text-sm text-muted">
          {live && away >= 0 && away <= 7 && <span className="font-medium text-fg">{t("when", { days: away })} · </span>}
          {appointment.locationMode === "AtDonorPlace" ? t("atHome") : t("atInstitution")} · {appointment.appointmentNo}
        </p>
        {appointment.address && <p className="text-sm text-fg">{appointment.address}</p>}
        {appointment.occasion && <p className="font-tagline text-base italic text-fg">{appointment.occasion}</p>}

        {appointment.status === "Rejected" && appointment.rejectReason && (
          <p className="rounded-cup bg-danger-soft px-3 py-2 text-sm text-fg">{t("rejectedBecause", { reason: appointment.rejectReason })}</p>
        )}
        {appointment.status === "Cancelled" && appointment.cancelReason && (
          <p className="rounded-cup bg-subtle px-3 py-2 text-sm text-fg">{t("cancelledBecause", { reason: appointment.cancelReason })}</p>
        )}
        {appointment.status === "Requested" && <p className="text-sm text-muted">{t("waiting")}</p>}

        {appointment.canCancel && (
          <div className="pt-1">
            <Button variant="ghost" onClick={onCancel} className="-ml-3 text-danger hover:bg-danger/10">
              {t("cancel")}
            </Button>
          </div>
        )}
      </div>
    </article>
  );
}

function CancelSheet({ appointment, onClose, onCancelled }: { appointment: Shown; onClose: () => void; onCancelled: () => void }) {
  const t = useTranslations("Services.mine.cancelSheet");
  const toast = useToast();
  const wide = useWide();
  const errorMessage = useServiceError();
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setFailure(null);
    const result = await cancelMyAppointment(appointment.appointmentNo, reason.trim() || null);
    setSubmitting(false);
    if (result.ok) {
      toast({ variant: "success", title: t("done", { appointmentNo: appointment.appointmentNo }) });
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
            <SheetDescription>{t("description", { appointmentNo: appointment.appointmentNo })}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-4">
            <Field label={t("reason")} description={t("reasonHint")}>
              <textarea value={reason} onChange={(event) => setReason(event.target.value)} maxLength={MAX_REASON} rows={3} className={cn(inputClasses, "resize-y")} />
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
