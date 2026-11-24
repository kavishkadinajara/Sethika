"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { Switch } from "@/components/bookings/Switch";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import type { BookingFailure } from "@/lib/bookings/api";
import { colomboToday, weekdayDate } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import { addTimeOff, createLeader, listLeaders, listTimeOff, removeTimeOff, setLeaderActive, setWeeklyHours, updateLeader } from "@/lib/services/api";
import { colomboParts, startTimes } from "@/lib/services/time";
import type { Leader, LeaderLanguage, TimeOff, WeeklyHours } from "@/lib/services/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { useServiceError } from "./useServiceError";

const LANGUAGES: LeaderLanguage[] = ["si", "ta", "en"];
const ALL_TIMES = startTimes("00:00", "23:45");

/**
 * The institution's religious leaders (Phase E, promise 3): who they are, how to text them, their
 * usual week, and their leave. A leader need never sign in (V1); one who does is linked by phone
 * (V21). Leave takes the leader's time, so it is refused over an appointment they hold (V19).
 */
export function LeadersScreen() {
  const t = useTranslations("Services.leaders");
  const locale = useLocale();
  const dates = useDates();
  const errorMessage = useServiceError();
  const { can } = usePermissions();
  const canCreate = can("SERVICES.LEADERS.CREATE");
  const canEdit = can("SERVICES.LEADERS.EDIT");

  const [leaders, setLeaders] = useState<Leader[] | null>(null);
  const [timeOff, setTimeOff] = useState<TimeOff[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [editing, setEditing] = useState<Leader | "new" | null>(null);
  const [week, setWeek] = useState<Leader | null>(null);
  const [leave, setLeave] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  /** Appointments a leader taken off the roster still holds (V23), to say so until dismissed. */
  const [kept, setKept] = useState<string[]>([]);

  const load = useCallback(async () => {
    setFailure(null);
    const [l, o] = await Promise.all([listLeaders(), listTimeOff()]);
    if (!l.ok) return setFailure(l.failure);
    if (!o.ok) return setFailure(o.failure);
    setLeaders(l.value);
    setTimeOff(o.value);
  }, []);

  useEffect(() => {
    // Loading the screen's data is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  async function toggle(leader: Leader) {
    setBusy(leader.publicId);
    const result = await setLeaderActive(leader.publicId, !leader.isActive);
    setBusy(null);
    if (result.ok && result.value.appointments.length > 0) setKept(result.value.appointments);
    if (!result.ok) setFailure(result.failure);
    void load();
  }

  async function removeLeave(id: number) {
    setBusy(`leave-${id}`);
    const result = await removeTimeOff(id);
    setBusy(null);
    if (!result.ok) setFailure(result.failure);
    void load();
  }

  if (failure && !leaders) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!leaders || !timeOff) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const name = (l: { displayName: string; nameSi: string | null; nameTa: string | null; title?: string | null }) =>
    [l.title, inLocale(locale, l.displayName, l.nameSi, l.nameTa)].filter(Boolean).join(" ");

  return (
    <div className="grid gap-10">
      <FormError>{failure && errorMessage(failure)}</FormError>

      {kept.length > 0 && (
        <div role="status" className="grid gap-2 rounded-cup border border-accent/60 bg-accent/10 px-4 py-3 text-fg">
          <p>{t("stillHolds", { appointments: kept.join(", ") })}</p>
          <Button variant="ghost" onClick={() => setKept([])} className="-ml-3 justify-self-start">
            {t("dismiss")}
          </Button>
        </div>
      )}

      <section className="grid gap-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-xl font-semibold text-brand">{t("heading")}</h2>
          {canCreate && <Button onClick={() => setEditing("new")}>{t("add")}</Button>}
        </div>

        {leaders.length === 0 ? (
          <EmptyState>{t("none")}</EmptyState>
        ) : (
          <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
            {leaders.map((leader) => (
              <li key={leader.publicId}>
                <article className={cn("grid h-full content-start gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift", !leader.isActive && "opacity-70")}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <h3 className="min-w-0 text-lg font-semibold leading-snug text-brand">{name(leader)}</h3>
                    {!leader.isActive && <span className="rounded-full border border-hairline bg-subtle px-3 py-1 text-sm text-muted">{t("offRoster")}</span>}
                  </div>
                  <p className="text-sm text-muted">
                    {leader.phone ?? t("noPhone")} · {t(`language.${leader.language}`)} · {leader.hasAccount ? t("signsIn") : t("noAccount")}
                  </p>
                  <p className="text-sm text-fg">
                    {leader.weeklyHours.length === 0
                      ? t("noWeek")
                      : leader.weeklyHours.map((h) => `${dates.weekday(weekdayDate(h.dayOfWeek), "short")} ${h.startTime}–${h.endTime}`).join(" · ")}
                  </p>
                  {canEdit && (
                    <div className="flex flex-wrap gap-2 pt-1">
                      <Button variant="outline" onClick={() => setEditing(leader)}>
                        {t("edit")}
                      </Button>
                      <Button variant="outline" onClick={() => setWeek(leader)}>
                        {t("week")}
                      </Button>
                      <Button variant="ghost" loading={busy === leader.publicId} onClick={() => void toggle(leader)}>
                        {leader.isActive ? t("deactivate") : t("activate")}
                      </Button>
                    </div>
                  )}
                </article>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="grid gap-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-xl font-semibold text-brand">{t("leaveHeading")}</h2>
          {canEdit && leaders.some((l) => l.isActive) && (
            <Button variant="outline" onClick={() => setLeave(true)}>
              {t("addLeave")}
            </Button>
          )}
        </div>
        {timeOff.length === 0 ? (
          <p className="text-muted">{t("noLeave")}</p>
        ) : (
          <ul className="grid gap-2">
            {timeOff.map((off) => {
              const from = colomboParts(off.startsAt);
              const to = colomboParts(off.endsAt);
              return (
                <li key={off.id} className="flex flex-wrap items-center justify-between gap-3 rounded-cup border border-hairline bg-surface px-4 py-3">
                  <span className="grid gap-0.5">
                    <span className="font-medium text-fg">{off.leaderName}</span>
                    <span className="text-sm text-muted">
                      {dates.medium(from.date)} {from.time} – {dates.medium(to.date)} {to.time}
                      {off.reason && <> · {off.reason}</>}
                    </span>
                  </span>
                  {canEdit && (
                    <Button variant="ghost" loading={busy === `leave-${off.id}`} onClick={() => void removeLeave(off.id)} className="text-danger hover:bg-danger/10">
                      {t("removeLeave")}
                    </Button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      {editing && (
        <LeaderSheet
          leader={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void load();
          }}
        />
      )}
      {week && (
        <WeekSheet
          leader={week}
          onClose={() => setWeek(null)}
          onSaved={() => {
            setWeek(null);
            void load();
          }}
        />
      )}
      {leave && (
        <LeaveSheet
          leaders={leaders.filter((l) => l.isActive)}
          name={name}
          onClose={() => setLeave(false)}
          onAdded={() => {
            setLeave(false);
            void load();
          }}
        />
      )}
    </div>
  );
}

/** A leader, new or edited; linking an account finds it by the phone typed (V21). */
function LeaderSheet({ leader, onClose, onSaved }: { leader: Leader | null; onClose: () => void; onSaved: () => void }) {
  const t = useTranslations("Services.leaders.form");
  const languages = useTranslations("Services.leaders.language");
  const wide = useWide();
  const errorMessage = useServiceError();
  const [displayName, setDisplayName] = useState(leader?.displayName ?? "");
  const [nameSi, setNameSi] = useState(leader?.nameSi ?? "");
  const [nameTa, setNameTa] = useState(leader?.nameTa ?? "");
  const [title, setTitle] = useState(leader?.title ?? "");
  const [phone, setPhone] = useState(leader?.phone ?? "");
  const [language, setLanguage] = useState<LeaderLanguage>(leader?.language ?? "si");
  const [linkAccount, setLinkAccount] = useState(leader?.hasAccount ?? false);
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  const error = (field: "displayName" | "nameSi" | "nameTa" | "title" | "phone" | "language") => (invalid.has(field) ? t(`errors.${field}`) : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    if (displayName.trim() === "") return setInvalid(new Set(["displayName"]));

    setSaving(true);
    const input = {
      displayName: displayName.trim(),
      nameSi: nameSi.trim() || null,
      nameTa: nameTa.trim() || null,
      title: title.trim() || null,
      phone: phone.trim() || null,
      language,
      linkAccount,
      version: leader?.version,
    };
    const result = leader ? await updateLeader(leader.publicId, input) : await createLeader(input);
    setSaving(false);

    if (result.ok) return onSaved();
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{leader ? t("editTitle") : t("newTitle")}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <div className="grid grid-cols-[minmax(0,8rem)_minmax(0,1fr)] gap-4">
              <Field label={t("title")} description={t("titleHint")} error={error("title")}>
                <Input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={40} autoComplete="off" />
              </Field>
              <Field label={t("displayName")} error={error("displayName")} required>
                <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} maxLength={150} autoComplete="off" />
              </Field>
            </div>
            <div className="grid gap-5 sm:grid-cols-2">
              <Field label={t("nameSi")} error={error("nameSi")}>
                <Input lang="si" value={nameSi} onChange={(e) => setNameSi(e.target.value)} maxLength={150} autoComplete="off" />
              </Field>
              <Field label={t("nameTa")} error={error("nameTa")}>
                <Input lang="ta" value={nameTa} onChange={(e) => setNameTa(e.target.value)} maxLength={150} autoComplete="off" />
              </Field>
            </div>
            <Field label={t("phone")} description={t("phoneHint")} error={error("phone")}>
              <Input type="tel" inputMode="tel" value={phone} onChange={(e) => setPhone(e.target.value)} maxLength={20} autoComplete="off" />
            </Field>
            <Field label={t("language")} description={t("languageHint")} error={error("language")}>
              <select value={language} onChange={(e) => setLanguage(e.target.value as LeaderLanguage)} className={cn(inputClasses, "w-48")}>
                {LANGUAGES.map((code) => (
                  <option key={code} value={code}>
                    {languages(code)}
                  </option>
                ))}
              </select>
            </Field>
            <Switch checked={linkAccount} onChange={setLinkAccount}>
              <span className="grid gap-0.5">
                <span className="font-medium">{t("linkAccount")}</span>
                <span className="text-sm text-muted">{t("linkAccountHint")}</span>
              </span>
            </Switch>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {leader ? t("save") : t("create")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/** The leader's usual week, replaced whole (V22): only advice for the assign sheet, never a rule (V2). */
function WeekSheet({ leader, onClose, onSaved }: { leader: Leader; onClose: () => void; onSaved: () => void }) {
  const t = useTranslations("Services.leaders.weekSheet");
  const dates = useDates();
  const wide = useWide();
  const errorMessage = useServiceError();
  const [spans, setSpans] = useState<WeeklyHours[]>(leader.weeklyHours);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  const change = (index: number, patch: Partial<WeeklyHours>) => setSpans((current) => current.map((s, i) => (i === index ? { ...s, ...patch } : s)));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setFailure(null);
    const result = await setWeeklyHours(leader.publicId, spans);
    setSaving(false);
    if (result.ok) onSaved();
    else setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("title", { leader: leader.displayName })}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-3">
            {spans.length === 0 && <p className="text-muted">{t("none")}</p>}
            {spans.map((span, index) => (
              <div key={index} className="grid grid-cols-[minmax(0,1fr)_auto_auto_auto] items-end gap-2">
                <Field label={t("day")}>
                  <select value={span.dayOfWeek} onChange={(e) => change(index, { dayOfWeek: Number(e.target.value) })} className={inputClasses}>
                    {[0, 1, 2, 3, 4, 5, 6].map((day) => (
                      <option key={day} value={day}>
                        {dates.weekday(weekdayDate(day), "long")}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label={t("from")}>
                  <select value={span.startTime} onChange={(e) => change(index, { startTime: e.target.value })} className={cn(inputClasses, "tabular-nums")}>
                    {ALL_TIMES.map((time) => (
                      <option key={time} value={time}>
                        {time}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label={t("to")}>
                  <select value={span.endTime} onChange={(e) => change(index, { endTime: e.target.value })} className={cn(inputClasses, "tabular-nums")}>
                    {ALL_TIMES.map((time) => (
                      <option key={time} value={time}>
                        {time}
                      </option>
                    ))}
                  </select>
                </Field>
                <Button variant="ghost" size="icon" aria-label={t("remove")} onClick={() => setSpans((current) => current.filter((_, i) => i !== index))}>
                  <span aria-hidden className="text-xl leading-none">×</span>
                </Button>
              </div>
            ))}
            <Button variant="outline" onClick={() => setSpans((current) => [...current, { dayOfWeek: 0, startTime: "06:00", endTime: "18:00" }])} className="justify-self-start">
              {t("addSpan")}
            </Button>
            <FormError>{failure && (failure.code === "invalid_details" ? t("invalid") : errorMessage(failure))}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {t("save")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/** Leave for one leader, at most 31 days (V18); refused over an appointment they hold (V19). */
function LeaveSheet({ leaders, name, onClose, onAdded }: { leaders: Leader[]; name: (l: Leader) => string; onClose: () => void; onAdded: () => void }) {
  const t = useTranslations("Services.leaders.leaveSheet");
  const wide = useWide();
  const errorMessage = useServiceError();
  const today = colomboToday();
  const [leader, setLeader] = useState(leaders[0]?.publicId ?? "");
  const [fromDate, setFromDate] = useState(today);
  const [fromTime, setFromTime] = useState("00:00");
  const [toDate, setToDate] = useState(today);
  const [toTime, setToTime] = useState("23:45");
  const [reason, setReason] = useState("");
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [fields, setFields] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setFailure(null);
    setFields([]);
    const result = await addTimeOff(leader, {
      startsAt: `${fromDate}T${fromTime}:00+05:30`,
      endsAt: `${toDate}T${toTime}:00+05:30`,
      reason: reason.trim() || null,
    });
    setSaving(false);
    if (result.ok) return onAdded();
    if (result.failure.code === "invalid_details") setFields(result.failure.fields);
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("title")}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <Field label={t("leader")}>
              <select value={leader} onChange={(e) => setLeader(e.target.value)} className={inputClasses}>
                {leaders.map((l) => (
                  <option key={l.publicId} value={l.publicId}>
                    {name(l)}
                  </option>
                ))}
              </select>
            </Field>
            <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-3">
              <Field label={t("from")} error={fields.includes("startsAt") ? t("invalidFrom") : null}>
                <Input type="date" min={today} value={fromDate} onChange={(e) => {
                  setFromDate(e.target.value);
                  if (toDate < e.target.value) setToDate(e.target.value);
                }} />
              </Field>
              <Field label={t("fromTime")}>
                <select value={fromTime} onChange={(e) => setFromTime(e.target.value)} className={cn(inputClasses, "tabular-nums")}>
                  {ALL_TIMES.map((time) => (
                    <option key={time} value={time}>
                      {time}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-3">
              <Field label={t("to")} error={fields.includes("endsAt") ? t("invalidTo") : null}>
                <Input type="date" min={fromDate || today} value={toDate} onChange={(e) => setToDate(e.target.value)} />
              </Field>
              <Field label={t("toTime")}>
                <select value={toTime} onChange={(e) => setToTime(e.target.value)} className={cn(inputClasses, "tabular-nums")}>
                  {ALL_TIMES.map((time) => (
                    <option key={time} value={time}>
                      {time}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            <Field label={t("reason")} description={t("reasonHint")}>
              <Input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={200} autoComplete="off" />
            </Field>
            <FormError>{failure && failure.code !== "invalid_details" && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {t("add")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
