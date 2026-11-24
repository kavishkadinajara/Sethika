"use client";

import { useLocale, useTranslations } from "next-intl";
import { useEffect, useMemo, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import type { BookingFailure } from "@/lib/bookings/api";
import { colomboToday, weekdayDate } from "@/lib/bookings/calendar";
import { localName } from "@/lib/bookings/names";
import type { FulfilmentMode, InstitutionMealSlot } from "@/lib/bookings/types";
import { createSponsorship, previewSponsorship } from "@/lib/sponsors/api";
import { patternFields, patternProblems, type PatternForm } from "@/lib/sponsors/pattern";
import type { CreatedSponsorship, Donor, PreviewDay, StandingPattern } from "@/lib/sponsors/types";
import { cn, focusRing } from "@/lib/utils";
import { useSponsorError } from "./useSponsorError";

const PATTERNS: StandingPattern[] = ["Once", "Daily", "Weekly", "Monthly", "Yearly"];
const INTERVALS = [1, 2, 3, 4, 6, 12];
const MONTH_DAYS = Array.from({ length: 31 }, (_, i) => i + 1);
const MONTHS = Array.from({ length: 12 }, (_, i) => i + 1);
const MAX_IN_MEMORY_OF = 200;
const MAX_NOTES = 500;
/** How long the picker waits after the last change before asking for the preview. */
const PREVIEW_DELAY_MS = 350;

/** The form's fields with their own message, as the API's `fields` names them. */
const FIELDS = ["donor", "mealSlot", "pattern", "weekDays", "monthDays", "monthInterval", "yearMonth", "yearDay", "startDate", "endDate", "headcount", "fulfilment", "inMemoryOf", "notes"] as const;
type FormField = (typeof FIELDS)[number];
const isField = (field: string): field is FormField => (FIELDS as readonly string[]).includes(field);

/** Why a day is not booked: an engine refusal, or what became of it while staff waited (K9). */
const CLASHES = ["slot_inactive", "institution_not_active", "not_on_this_day", "out_of_season", "date_blocked", "too_soon", "too_far", "slot_full", "held", "alreadyHeld", "alreadyDone", "ended", "busy"] as const;
type Clash = (typeof CLASHES)[number];
/** An unknown code reads as the plainest refusal rather than a missing message. */
const clashOf = (code: string | null): Clash => ((CLASHES as readonly string[]).includes(code ?? "") ? (code as Clash) : "slot_inactive");

const toggle = (list: number[], value: number) => (list.includes(value) ? list.filter((v) => v !== value) : [...list, value]);

/**
 * Setting up a donor's regular days (plan 08, K2, K9): the meal, a pattern (one day, every day, some
 * weekdays, some days of every N months, a day each year), and the meal's details. While staff choose,
 * the next days are shown with any the engine would refuse now; saving keeps the clashes as not
 * booked rather than refusing the arrangement, and says what became of each day.
 */
export function NewSponsorshipSheet({ donor, slots, onClose, onSaved }: { donor: Donor; slots: InstitutionMealSlot[]; onClose: () => void; onSaved: () => void }) {
  const t = useTranslations("Sponsors.new");
  const booking = useTranslations("Booking");
  const locale = useLocale();
  const wide = useWide();
  const dates = useDates();
  const errorMessage = useSponsorError();
  const today = colomboToday();
  const active = useMemo(() => slots.filter((s) => s.isActive), [slots]);

  const [slotId, setSlotId] = useState(active[0]?.publicId ?? "");
  const slot = active.find((s) => s.publicId === slotId) ?? null;
  const [form, setForm] = useState<PatternForm>({
    pattern: "Monthly",
    weekDays: [],
    monthDays: [],
    monthInterval: 1,
    yearMonth: Number(today.slice(5, 7)),
    yearDay: Number(today.slice(8, 10)),
    startDate: today,
    endDate: "",
  });
  const [headcount, setHeadcount] = useState(String(slot?.minHeadcount ?? 1));
  const [fulfilment, setFulfilment] = useState<FulfilmentMode | "">(slot?.allowedFulfilment[0] ?? "");
  const [inMemoryOf, setInMemoryOf] = useState("");
  const [notes, setNotes] = useState("");
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);
  const [done, setDone] = useState<CreatedSponsorship | null>(null);

  // A change answers that field's complaint: the message goes until the next save.
  const set = <K extends keyof PatternForm>(key: K, value: PatternForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    setInvalid((current) => {
      if (!current.has(key)) return current;
      const next = new Set(current);
      next.delete(key);
      return next;
    });
  };

  const problems = patternProblems(form, today);
  const fields = patternFields(form);
  const ready = slot !== null && problems.length === 0;
  const previewKey = ready ? JSON.stringify({ slotId, fields }) : null;

  // The preview follows the picker, a moment after the last change; an answer for older choices is dropped.
  const [preview, setPreview] = useState<{ key: string; days: PreviewDay[] | null; failed: boolean } | null>(null);
  useEffect(() => {
    if (!previewKey) return;
    let stale = false;
    const timer = setTimeout(() => {
      void previewSponsorship({ mealSlot: slotId, ...fields }).then((result) => {
        if (stale) return;
        setPreview({ key: previewKey, days: result.ok ? result.value.days : null, failed: !result.ok });
      });
    }, PREVIEW_DELAY_MS);
    return () => {
      stale = true;
      clearTimeout(timer);
    };
    // `fields` and `slotId` are what `previewKey` spells out.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewKey]);
  const shown = preview && preview.key === previewKey ? preview : null;

  function chooseSlot(id: string) {
    setSlotId(id);
    const next = active.find((s) => s.publicId === id);
    if (next) {
      setHeadcount(String(next.minHeadcount));
      setFulfilment(next.allowedFulfilment[0] ?? "");
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    const people = Number(headcount);
    const missing = new Set<string>(problems);
    if (!slot) missing.add("mealSlot");
    if (!/^\d+$/.test(headcount.trim()) || (slot && (people < slot.minHeadcount || people > slot.maxHeadcount))) missing.add("headcount");
    if (!fulfilment) missing.add("fulfilment");
    setInvalid(missing);
    if (missing.size > 0 || !slot || !fulfilment) return;

    setSaving(true);
    const result = await createSponsorship({
      donor: donor.publicId,
      mealSlot: slot.publicId,
      ...fields,
      headcount: people,
      fulfilment,
      inMemoryOf: inMemoryOf.trim() || null,
      notes: notes.trim() || null,
    });
    setSaving(false);

    if (result.ok) {
      setDone(result.value);
      onSaved();
      return;
    }
    if (result.failure.code === "invalid_details" && result.failure.fields.some(isField)) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  const error = (field: FormField) => (invalid.has(field) ? t(`errors.${field}`) : null);
  const fieldsetError = (field: FormField) => invalid.has(field) && <p className="text-sm text-danger">{t(`errors.${field}`)}</p>;

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-xl" : undefined}>
        <SheetHeader>
          <SheetTitle>{t("title", { name: donor.fullName })}</SheetTitle>
          <SheetDescription>{t("description")}</SheetDescription>
        </SheetHeader>

        {done ? (
          <Created result={done} onClose={onClose} />
        ) : active.length === 0 ? (
          <SheetBody>
            <p className="text-fg">{t("noSlots")}</p>
          </SheetBody>
        ) : (
          <form onSubmit={submit} noValidate className="contents">
            <SheetBody className="grid gap-5">
              <Field label={t("meal")} error={error("mealSlot")} required>
                <select value={slotId} onChange={(e) => chooseSlot(e.target.value)} className={inputClasses}>
                  {active.map((s) => (
                    <option key={s.publicId} value={s.publicId}>
                      {localName(locale, s)} · {s.startTime}
                    </option>
                  ))}
                </select>
              </Field>

              <fieldset className="grid gap-2">
                <legend className="mb-1 text-base font-medium text-fg">{t("pattern")}</legend>
                <div role="radiogroup" className="flex flex-wrap gap-2">
                  {PATTERNS.map((pattern) => (
                    <label
                      key={pattern}
                      className={cn(
                        "flex min-h-11 cursor-pointer items-center gap-2 rounded-full border-[1.5px] px-4 text-sm font-medium",
                        "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                        form.pattern === pattern ? "border-brand bg-brand/6 text-brand" : "border-hairline bg-surface text-fg",
                      )}
                    >
                      <input type="radio" name="pattern" checked={form.pattern === pattern} onChange={() => set("pattern", pattern)} className="sr-only" />
                      {t(`patterns.${pattern}`)}
                    </label>
                  ))}
                </div>
                {fieldsetError("pattern")}
              </fieldset>

              {form.pattern === "Weekly" && (
                <fieldset className="grid gap-2">
                  <legend className="mb-1 text-base font-medium text-fg">{t("weekDays")}</legend>
                  <div className="flex flex-wrap gap-2">
                    {[0, 1, 2, 3, 4, 5, 6].map((day) => (
                      <ToggleChip key={day} on={form.weekDays.includes(day)} onClick={() => set("weekDays", toggle(form.weekDays, day))}>
                        {dates.weekday(weekdayDate(day), "short")}
                      </ToggleChip>
                    ))}
                  </div>
                  {fieldsetError("weekDays")}
                </fieldset>
              )}

              {form.pattern === "Monthly" && (
                <>
                  <fieldset className="grid gap-2">
                    <legend className="mb-1 text-base font-medium text-fg">{t("monthDays")}</legend>
                    <p className="text-sm text-muted">{t("monthDaysHint")}</p>
                    <div className="grid grid-cols-7 gap-1.5 sm:max-w-sm">
                      {MONTH_DAYS.map((day) => (
                        <ToggleChip key={day} on={form.monthDays.includes(day)} onClick={() => set("monthDays", toggle(form.monthDays, day))} square>
                          {day}
                        </ToggleChip>
                      ))}
                    </div>
                    {fieldsetError("monthDays")}
                  </fieldset>
                  <Field label={t("interval")} description={t("intervalHint")} error={error("monthInterval")}>
                    <select value={form.monthInterval} onChange={(e) => set("monthInterval", Number(e.target.value))} className={cn(inputClasses, "w-60")}>
                      {INTERVALS.map((n) => (
                        <option key={n} value={n}>
                          {t("intervals", { n })}
                        </option>
                      ))}
                    </select>
                  </Field>
                </>
              )}

              {form.pattern === "Yearly" && (
                <fieldset className="grid gap-2">
                  <legend className="mb-1 text-base font-medium text-fg">{t("yearDate")}</legend>
                  <p className="text-sm text-muted">{t("yearHint")}</p>
                  <div className="flex flex-wrap gap-3">
                    <select aria-label={t("yearMonth")} value={form.yearMonth} onChange={(e) => set("yearMonth", Number(e.target.value))} className={cn(inputClasses, "w-48")}>
                      {MONTHS.map((m) => (
                        <option key={m} value={m}>
                          {dates.monthShort(`2000-${String(m).padStart(2, "0")}-01`)}
                        </option>
                      ))}
                    </select>
                    <select aria-label={t("yearDay")} value={form.yearDay} onChange={(e) => set("yearDay", Number(e.target.value))} className={cn(inputClasses, "w-24")}>
                      {MONTH_DAYS.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                  </div>
                  {fieldsetError("yearMonth")}
                  {fieldsetError("yearDay")}
                </fieldset>
              )}

              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={form.pattern === "Once" ? t("onDate") : t("startDate")} error={error("startDate")} required>
                  <Input type="date" min={today} value={form.startDate} onChange={(e) => set("startDate", e.target.value)} />
                </Field>
                {form.pattern !== "Once" && (
                  <Field label={t("endDate")} description={t("endHint")} error={error("endDate")}>
                    <Input type="date" min={form.startDate || today} value={form.endDate} onChange={(e) => set("endDate", e.target.value)} />
                  </Field>
                )}
              </div>

              <Preview ready={ready} shown={shown} />

              <Field
                label={t("headcount")}
                description={slot ? t("headcountHint", { min: slot.minHeadcount, max: slot.maxHeadcount }) : undefined}
                error={error("headcount")}
                required
              >
                <Input inputMode="numeric" value={headcount} onChange={(e) => setHeadcount(e.target.value)} className="w-28" />
              </Field>

              {slot && (
                <fieldset className="grid gap-2">
                  <legend className="mb-1 text-base font-medium text-fg">{t("fulfilment")}</legend>
                  {slot.allowedFulfilment.map((mode) => (
                    <label key={mode} className="flex min-h-11 cursor-pointer items-center gap-3 text-fg">
                      <input type="radio" name="fulfilment" checked={fulfilment === mode} onChange={() => setFulfilment(mode)} className="size-5 accent-[var(--color-brand)]" />
                      {booking(`fulfilment.${mode}`)}
                    </label>
                  ))}
                  {fieldsetError("fulfilment")}
                </fieldset>
              )}

              <Field label={t("inMemoryOf")} error={error("inMemoryOf")}>
                <Input value={inMemoryOf} onChange={(e) => setInMemoryOf(e.target.value)} maxLength={MAX_IN_MEMORY_OF} autoComplete="off" />
              </Field>
              <Field label={t("notes")} error={error("notes")}>
                <textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={MAX_NOTES} rows={2} className={cn(inputClasses, "resize-y")} />
              </Field>
              {invalid.has("donor") && <FormError>{t("errors.donor")}</FormError>}
              <FormError>{failure && errorMessage(failure)}</FormError>
            </SheetBody>
            <SheetFooter>
              <Button variant="ghost" onClick={onClose}>
                {t("cancel")}
              </Button>
              <Button type="submit" loading={saving}>
                {t("create")}
              </Button>
            </SheetFooter>
          </form>
        )}
      </SheetContent>
    </Sheet>
  );
}

function ToggleChip({ on, onClick, square = false, children }: { on: boolean; onClick: () => void; square?: boolean; children: React.ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={on}
      onClick={onClick}
      className={cn(
        // A month's 31 days fill a 7-column grid: on a 320 px phone a cell is under 44 px wide.
        "min-h-11 border-[1.5px] text-sm font-medium tabular-nums transition-colors duration-150",
        square ? "rounded-cup px-0" : "min-w-11 rounded-full px-3",
        focusRing,
        on ? "border-primary bg-primary text-on-primary" : "border-line bg-surface text-fg hover:border-brand",
      )}
    >
      {children}
    </button>
  );
}

/** The next days as the calendar stands now: free, a clash and why, or booked when the window reaches it. */
function Preview({ ready, shown }: { ready: boolean; shown: { days: PreviewDay[] | null; failed: boolean } | null }) {
  const t = useTranslations("Sponsors.new");
  const clash = useTranslations("Sponsors.clash");
  const dates = useDates();

  return (
    <section aria-live="polite" aria-labelledby="standing-preview" className="grid gap-2 rounded-cup border border-hairline bg-subtle/50 p-4">
      <h3 id="standing-preview" className="font-semibold text-brand">
        {t("preview")}
      </h3>
      {!ready ? (
        <p className="text-sm text-muted">{t("previewWaiting")}</p>
      ) : !shown ? (
        <div className="flex justify-center py-2 text-brand">
          <Spinner />
        </div>
      ) : shown.failed || !shown.days ? (
        <p className="text-sm text-muted">{t("previewFailed")}</p>
      ) : shown.days.length === 0 ? (
        <p className="text-sm text-muted">{t("previewNone")}</p>
      ) : (
        <>
          <p className="text-sm text-muted">{t("previewHint")}</p>
          <ul className="grid gap-1">
            {shown.days.map((day) => (
              <li key={day.date} className="flex flex-wrap items-baseline justify-between gap-x-3 text-sm" data-state={day.state}>
                <span className="tabular-nums text-fg">{dates.dayMonth(day.date)} {day.date.slice(0, 4)}</span>
                <span className={cn(day.state === "clash" ? "font-medium text-danger" : "text-muted")}>
                  {day.state === "clash" ? clash(clashOf(day.reason)) : t(day.state)}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

/** What became of the days booked while staff waited, and how many the job books next. */
function Created({ result, onClose }: { result: CreatedSponsorship; onClose: () => void }) {
  const t = useTranslations("Sponsors.new.done");
  const clash = useTranslations("Sponsors.clash");
  const dates = useDates();
  const booked = result.days.filter((d) => d.outcome === "booked");
  const missed = result.days.filter((d) => d.outcome !== "booked");

  return (
    <>
      <SheetBody className="grid gap-4">
        <p role="status" className="text-lg font-semibold text-brand">
          {t("booked", { count: booked.length })}
        </p>
        {missed.length > 0 && (
          <div className="grid gap-1">
            <p className="font-medium text-fg">{t("notBooked")}</p>
            <ul className="grid gap-1">
              {missed.map((day) => (
                <li key={day.date} className="flex flex-wrap justify-between gap-x-3 text-sm">
                  <span className="tabular-nums text-fg">{dates.dayMonth(day.date)}</span>
                  <span className="text-danger">{clash(clashOf(day.outcome === "notBooked" ? day.error : day.outcome))}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
        {result.later > 0 && <p className="text-muted">{t("later", { count: result.later })}</p>}
      </SheetBody>
      <SheetFooter>
        <Button onClick={onClose}>{t("close")}</Button>
      </SheetFooter>
    </>
  );
}
