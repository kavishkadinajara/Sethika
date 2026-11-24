"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import type { BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import { assignLeaders, decideAppointment, getAgenda, getCandidates, removeLeader, respondOnBehalf } from "@/lib/services/api";
import { colomboParts } from "@/lib/services/time";
import type { AgendaAppointment, AgendaView, AppointmentLeader, LeaderCandidate } from "@/lib/services/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { ServiceStatusBadge } from "./ServiceStatusBadge";
import { useServiceError } from "./useServiceError";

const VIEWS: AgendaView[] = ["upcoming", "waiting", "needs-leaders", "history"];
const MAX_REASON = 300;

/** What a reason sheet is for: the institution's decisions, and a leader's decline given for them. */
type Asking =
  | { kind: "reject" | "cancel"; appointment: AgendaAppointment }
  | { kind: "decline"; appointment: AgendaAppointment; leader: AppointmentLeader };

/**
 * The institution's service appointments as a day-by-day agenda (V10): what waits for a decision,
 * what is short of leaders, what is coming, what has been. From here leaders are assigned, removed
 * and answered for, and requests rejected, cancelled or marked as held.
 */
export function AppointmentsScreen() {
  const t = useTranslations("Services.agenda");
  const dates = useDates();
  const errorMessage = useServiceError();
  const toast = useToast();
  const [view, setView] = useState<AgendaView>("upcoming");
  const [appointments, setAppointments] = useState<AgendaAppointment[] | null>(null);
  // When the list was read: "has it ended" is judged against this, not the clock during a render.
  const [loadedAt, setLoadedAt] = useState(0);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [assigning, setAssigning] = useState<AgendaAppointment | null>(null);
  const [asking, setAsking] = useState<Asking | null>(null);

  const load = useCallback(async (wanted: AgendaView) => {
    setFailure(null);
    const result = await getAgenda(wanted);
    if (result.ok) {
      setAppointments(result.value);
      setLoadedAt(Date.now());
    } else setFailure(result.failure);
  }, []);

  useEffect(() => {
    // Loading the chosen view is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(view);
  }, [load, view]);

  /** Runs a change, says how it went, and reloads the view either way: someone may have changed it first. */
  async function act(change: () => Promise<{ ok: true } | { ok: false; failure: BookingFailure }>, done: string) {
    const result = await change();
    if (result.ok) toast({ variant: "success", title: done });
    else toast({ variant: "error", title: errorMessage(result.failure) });
    void load(view);
  }

  const days = groupByDay(appointments ?? []);

  return (
    <div className="grid gap-6">
      <div role="group" aria-label={t("viewLabel")} className="flex flex-wrap gap-2">
        {VIEWS.map((v) => (
          <button
            key={v}
            type="button"
            aria-pressed={view === v}
            onClick={() => {
              setAppointments(null);
              setView(v);
            }}
            className={cn(
              "min-h-11 rounded-full border-[1.5px] px-4 text-sm font-medium transition-colors duration-150",
              focusRing,
              view === v ? "border-primary bg-primary text-on-primary" : "border-line bg-surface text-fg hover:border-brand",
            )}
          >
            {t(`views.${v}`)}
          </button>
        ))}
      </div>

      {failure && (
        <FormError>
          {errorMessage(failure)}{" "}
          <button type="button" onClick={() => void load(view)} className={cn(textLink, focusRing, "min-h-0")}>
            {t("retry")}
          </button>
        </FormError>
      )}

      {!appointments && !failure && (
        <div className="flex justify-center py-16 text-brand">
          <Spinner />
        </div>
      )}

      {appointments && appointments.length === 0 && <EmptyState>{t(`empty.${view}`)}</EmptyState>}

      {days.map(({ date, items }) => (
        <section key={date} className="grid gap-3">
          <h2 className="text-lg font-semibold text-brand">{dates.full(date)}</h2>
          <ul className="grid gap-3 xl:grid-cols-2">
            {items.map((appointment) => (
              <li key={appointment.appointmentNo}>
                <AppointmentCard
                  appointment={appointment}
                  now={loadedAt}
                  onAssign={() => setAssigning(appointment)}
                  onAsk={setAsking}
                  onAct={act}
                />
              </li>
            ))}
          </ul>
        </section>
      ))}

      {assigning && (
        <AssignSheet
          appointment={assigning}
          onClose={() => setAssigning(null)}
          onAssigned={() => {
            setAssigning(null);
            toast({ variant: "success", title: t("assigned", { appointmentNo: assigning.appointmentNo }) });
            void load(view);
          }}
        />
      )}

      {asking && (
        <ReasonSheet
          asking={asking}
          onClose={() => setAsking(null)}
          onDone={(message) => {
            setAsking(null);
            toast({ variant: "success", title: message });
            void load(view);
          }}
        />
      )}
    </div>
  );
}

function AppointmentCard({
  appointment,
  now,
  onAssign,
  onAsk,
  onAct,
}: {
  appointment: AgendaAppointment;
  now: number;
  onAssign: () => void;
  onAsk: (asking: Asking) => void;
  onAct: (change: () => Promise<{ ok: true } | { ok: false; failure: BookingFailure }>, done: string) => Promise<void>;
}) {
  const t = useTranslations("Services.agenda");
  const locale = useLocale();
  const { can } = usePermissions();
  const canApprove = can("SERVICES.APPOINTMENTS.APPROVE");
  const canReject = can("SERVICES.APPOINTMENTS.REJECT");
  const canEdit = can("SERVICES.APPOINTMENTS.EDIT");

  const start = colomboParts(appointment.startsAt);
  const end = colomboParts(appointment.endsAt);
  const live = appointment.status === "Requested" || appointment.status === "Approved";
  const ended = new Date(appointment.endsAt).getTime() <= now;
  const liveLeaders = appointment.leaders.filter((l) => l.response !== "Declined").length;
  const no = appointment.appointmentNo;

  return (
    <article className="grid gap-2 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h3 className="min-w-0 text-lg font-semibold leading-snug text-fg">
          <span className="tabular-nums text-brand">
            {start.time}–{end.time}
          </span>{" "}
          {inLocale(locale, appointment.serviceName, appointment.serviceNameSi, appointment.serviceNameTa)}
        </h3>
        <div className="flex flex-wrap gap-2">
          {appointment.needsLeaders && (
            <span className="rounded-full border border-accent/60 bg-accent/15 px-3 py-1 text-sm font-medium text-fg">
              {t("needs", { count: appointment.leadersRequired - liveLeaders })}
            </span>
          )}
          <ServiceStatusBadge status={appointment.status} />
        </div>
      </div>

      <p className="text-sm text-fg">
        {appointment.donorName} ·{" "}
        <a href={`tel:${appointment.donorPhone}`} className={cn(textLink, focusRing, "min-h-0")}>
          {appointment.donorPhone}
        </a>{" "}
        · <span className="text-muted">{no}</span>
      </p>
      <p className="text-sm text-muted">{appointment.locationMode === "AtDonorPlace" ? t("atDonor", { address: appointment.address ?? "" }) : t("atInstitution")}</p>
      {appointment.occasion && <p className="font-tagline text-base italic text-fg">{appointment.occasion}</p>}
      {appointment.donorNote && <p className="rounded-cup bg-subtle px-3 py-2 text-sm text-fg">{appointment.donorNote}</p>}
      {appointment.status === "Rejected" && appointment.rejectReason && <p className="text-sm text-muted">{t("rejectedBecause", { reason: appointment.rejectReason })}</p>}
      {appointment.status === "Cancelled" && appointment.cancelReason && <p className="text-sm text-muted">{t("cancelledBecause", { reason: appointment.cancelReason })}</p>}

      {appointment.leaders.length > 0 && (
        <ul className="grid gap-1.5 border-t border-hairline pt-2">
          {appointment.leaders.map((leader) => (
            <li key={leader.publicId} className="flex flex-wrap items-center justify-between gap-2">
              <span className="grid gap-0.5">
                <span className="font-medium text-fg">{[leader.title, inLocale(locale, leader.displayName, leader.nameSi, leader.nameTa)].filter(Boolean).join(" ")}</span>
                <span className="text-sm text-muted">
                  {t(`response.${leader.response}`)}
                  {leader.respondedOnBehalf && <> · {t("onBehalf")}</>}
                  {leader.declineReason && <> · {leader.declineReason}</>}
                </span>
              </span>
              {canEdit && live && !ended && leader.response !== "Declined" && (
                <span className="flex flex-wrap gap-1">
                  {leader.response === "Pending" && appointment.status === "Approved" && (
                    <Button variant="ghost" onClick={() => void onAct(() => respondOnBehalf(no, leader.publicId, true, null), t("acceptedFor", { leader: leader.displayName }))}>
                      {t("acceptFor")}
                    </Button>
                  )}
                  {appointment.status === "Approved" && (
                    <Button variant="ghost" onClick={() => onAsk({ kind: "decline", appointment, leader })}>
                      {t("declineFor")}
                    </Button>
                  )}
                  <Button variant="ghost" className="text-danger hover:bg-danger/10" onClick={() => void onAct(() => removeLeader(no, leader.publicId), t("removed", { leader: leader.displayName }))}>
                    {t("remove")}
                  </Button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}

      {live && (
        <div className="flex flex-wrap gap-2 pt-1">
          {canApprove && !ended && appointment.needsLeaders && <Button onClick={onAssign}>{appointment.status === "Requested" ? t("assignApprove") : t("assignMore")}</Button>}
          {canReject && appointment.status === "Requested" && (
            <Button variant="outline" onClick={() => onAsk({ kind: "reject", appointment })}>
              {t("reject")}
            </Button>
          )}
          {canEdit && appointment.status === "Approved" && ended && (
            <Button variant="outline" onClick={() => void onAct(() => decideAppointment(no, "complete"), t("completed", { appointmentNo: no }))}>
              {t("complete")}
            </Button>
          )}
          {canEdit && !ended && (
            <Button variant="ghost" className="text-danger hover:bg-danger/10" onClick={() => onAsk({ kind: "cancel", appointment })}>
              {t("cancel")}
            </Button>
          )}
        </div>
      )}
    </article>
  );
}

/** Choosing leaders for one appointment: the picker shows who is free and normally available (V20). */
function AssignSheet({ appointment, onClose, onAssigned }: { appointment: AgendaAppointment; onClose: () => void; onAssigned: () => void }) {
  const t = useTranslations("Services.agenda.assignSheet");
  const locale = useLocale();
  const wide = useWide();
  const errorMessage = useServiceError();
  const [candidates, setCandidates] = useState<LeaderCandidate[] | null>(null);
  const [chosen, setChosen] = useState<string[]>([]);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);
  const room = appointment.leadersRequired - appointment.leaders.filter((l) => l.response !== "Declined").length;

  const loadCandidates = useCallback(async () => {
    const result = await getCandidates(appointment.appointmentNo);
    if (result.ok) setCandidates(result.value);
    else setFailure(result.failure);
  }, [appointment.appointmentNo]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadCandidates();
  }, [loadCandidates]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (chosen.length === 0) return;
    setSaving(true);
    setFailure(null);
    const result = await assignLeaders(appointment.appointmentNo, chosen);
    setSaving(false);
    if (result.ok) return onAssigned();
    setFailure(result.failure);
    // Who is free changed under the sheet: show it as it is now.
    if (result.failure.code === "leader_not_free" || result.failure.code === "try_again") {
      setChosen([]);
      void loadCandidates();
    }
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <form onSubmit={submit} className="contents">
          <SheetHeader>
            <SheetTitle>{t("title")}</SheetTitle>
            <SheetDescription>{t("description", { count: room, appointmentNo: appointment.appointmentNo })}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-2">
            {!candidates && !failure && (
              <div className="flex justify-center py-8 text-brand">
                <Spinner />
              </div>
            )}
            {candidates?.length === 0 && <p className="text-muted">{t("none")}</p>}
            {candidates?.map((candidate) => {
              const unavailable = candidate.assigned || candidate.busy !== null;
              const checked = chosen.includes(candidate.publicId);
              const full = !checked && chosen.length >= room;
              return (
                <label
                  key={candidate.publicId}
                  className={cn(
                    "flex min-h-14 items-start gap-3 rounded-cup border-[1.5px] bg-surface px-4 py-3",
                    "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                    unavailable || full ? "cursor-not-allowed opacity-60" : "cursor-pointer",
                    checked ? "border-brand bg-brand/6" : "border-hairline",
                  )}
                >
                  <input
                    type="checkbox"
                    checked={checked}
                    disabled={unavailable || full}
                    onChange={(e) => setChosen((current) => (e.target.checked ? [...current, candidate.publicId] : current.filter((id) => id !== candidate.publicId)))}
                    className="mt-1 size-5 accent-[var(--color-brand)]"
                  />
                  <span className="grid gap-0.5">
                    <span className="font-medium text-fg">{[candidate.title, inLocale(locale, candidate.displayName, candidate.nameSi, candidate.nameTa)].filter(Boolean).join(" ")}</span>
                    <span className="text-sm text-muted">
                      {candidate.assigned
                        ? t("assigned")
                        : candidate.busy === "TimeOff"
                          ? t("onLeave")
                          : candidate.busy === "Appointment"
                            ? t("elsewhere")
                            : candidate.normallyAvailable === false
                              ? t("outsideWeek")
                              : t("free")}
                    </span>
                  </span>
                </label>
              );
            })}
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving} disabled={chosen.length === 0}>
              {t("assign", { count: chosen.length })}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/** A reason for a rejection or a cancellation (required), or for a leader's decline (optional). */
function ReasonSheet({ asking, onClose, onDone }: { asking: Asking; onClose: () => void; onDone: (message: string) => void }) {
  const t = useTranslations("Services.agenda.reasonSheet");
  const wide = useWide();
  const errorMessage = useServiceError();
  const [reason, setReason] = useState("");
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [missing, setMissing] = useState(false);
  const [saving, setSaving] = useState(false);
  const required = asking.kind !== "decline";
  const no = asking.appointment.appointmentNo;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (required && reason.trim() === "") return setMissing(true);
    setSaving(true);
    setFailure(null);
    const result =
      asking.kind === "decline"
        ? await respondOnBehalf(no, asking.leader.publicId, false, reason.trim() || null)
        : await decideAppointment(no, asking.kind, reason.trim());
    setSaving(false);
    if (result.ok) onDone(t(`${asking.kind}.done`, { appointmentNo: no }));
    else setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t(`${asking.kind}.title`)}</SheetTitle>
            <SheetDescription>
              {asking.kind === "decline" ? t("decline.description", { leader: asking.leader.displayName, appointmentNo: no }) : t(`${asking.kind}.description`, { appointmentNo: no })}
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-4">
            <Field label={required ? t("reason") : t("reasonOptional")} description={t(`${asking.kind}.hint`)} error={missing ? t("reasonRequired") : null} required={required}>
              <textarea
                value={reason}
                onChange={(e) => {
                  setReason(e.target.value);
                  setMissing(false);
                }}
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
            <Button type="submit" variant="danger" loading={saving}>
              {t(`${asking.kind}.confirm`)}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/** The appointments by Colombo day, in the order the API gave them (soonest or latest first). */
function groupByDay(appointments: AgendaAppointment[]): { date: string; items: AgendaAppointment[] }[] {
  const days: { date: string; items: AgendaAppointment[] }[] = [];
  for (const appointment of appointments) {
    const date = colomboParts(appointment.startsAt).date;
    const last = days.at(-1);
    if (last?.date === date) last.items.push(appointment);
    else days.push({ date, items: [appointment] });
  }
  return days;
}
