"use client";

import { useFormatter, useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { CheckCircleIcon } from "@/components/ui/icons";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Link } from "@/i18n/navigation";
import { book, type BookingFailure } from "@/lib/bookings/api";
import { memorialSearch, type Memorial } from "@/lib/bookings/find-query";
import { localName } from "@/lib/bookings/names";
import type { BookingStatus, DayAvailability, FulfilmentMode, PublicInstitution, PublicMealSlot } from "@/lib/bookings/types";
import { cn } from "@/lib/utils";
import { DonorSheetGate } from "./DonorSheetGate";
import { Switch } from "./Switch";
import { useBookingError } from "./useBookingError";
import { useWide } from "./useWide";
import { useDates } from "./useDates";

/** Refusals that mean the day was taken or closed while the donor looked at it. */
const dayGone = new Set(["slot_full", "already_booked", "date_blocked", "too_soon", "slot_inactive", "institution_not_active"]);

const MAX_IN_MEMORY_OF = 200;
const MAX_NOTES = 500;

type BookingSheetProps = {
  institution: PublicInstitution;
  slot: PublicMealSlot;
  day: DayAvailability;
  onClose: () => void;
  /** A booking was made: the month's bowls change. */
  onBooked: () => void;
  /** The day went while the form was open: close, and show the month as it is now. */
  onTaken: () => void;
  /** The memorial observance this day is booked for (plan 07, H6), when the donor came from the planner. */
  memorial?: Memorial | null;
};

/**
 * Booking one slot-day: who will be fed, who brings the meal, in whose memory. A bottom sheet a
 * thumb reaches on a phone, a side sheet on a wider screen. It asks for a sign-in only here, at the
 * moment it is needed, and the sign-in returns to this meal and day.
 */
export function BookingSheet({ institution, slot, day, onClose, onBooked, onTaken, memorial = null }: BookingSheetProps) {
  const t = useTranslations("Discover.sheet");
  const locale = useLocale();
  const wide = useWide();
  const dates = useDates();
  const [done, setDone] = useState<{ bookingNo: string; status: BookingStatus } | null>(null);

  const when = dates.full(day.date);

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <SheetHeader>
          <SheetTitle>{t("title", { meal: localName(locale, slot) })}</SheetTitle>
          <SheetDescription>
            {when} · {slot.startTime}–{slot.endTime} · {localName(locale, institution)}
          </SheetDescription>
        </SheetHeader>

        {done ? (
          <Done bookingNo={done.bookingNo} status={done.status} memorial={memorial !== null} onClose={onClose} />
        ) : (
          <Gate institution={institution} slot={slot} date={day.date} memorial={memorial}>
            <BookingForm
              slot={slot}
              day={day}
              memorial={memorial}
              onBooked={(result) => {
                setDone(result);
                onBooked();
              }}
              onTaken={onTaken}
            />
          </Gate>
        )}
      </SheetContent>
    </Sheet>
  );
}

/** Booking needs a donor's own session (plan 02, B1); say how to get one, and come back here. */
function Gate({
  institution,
  slot,
  date,
  memorial,
  children,
}: {
  institution: PublicInstitution;
  slot: PublicMealSlot;
  date: string;
  memorial: Memorial | null;
  children: ReactNode;
}) {
  const t = useTranslations("Discover.sheet");
  return (
    <DonorSheetGate
      back={`/i/${institution.slug}?slot=${encodeURIComponent(slot.publicId)}&date=${date}${memorialSearch(memorial)}`}
      permission="MYBOOKINGS.BOOKINGS.CREATE"
      signInFirst={t("signInFirst")}
      institutionSession={t("institutionSession")}
      notAllowed={t("notAllowed")}
    >
      {children}
    </DonorSheetGate>
  );
}

function BookingForm({
  slot,
  day,
  memorial,
  onBooked,
  onTaken,
}: {
  slot: PublicMealSlot;
  day: DayAvailability;
  memorial: Memorial | null;
  onBooked: (result: { bookingNo: string; status: BookingStatus }) => void;
  onTaken: () => void;
}) {
  const t = useTranslations("Discover.sheet");
  const booking = useTranslations("Booking");
  const format = useFormatter();
  const errorMessage = useBookingError();

  // The most one booking may bring: the slot's own limit, or what the day has left if less.
  const max = Math.max(slot.minHeadcount, Math.min(slot.maxHeadcount, day.headcount));
  const [headcount, setHeadcount] = useState(String(slot.minHeadcount));
  const [fulfilment, setFulfilment] = useState<FulfilmentMode>(slot.allowedFulfilment[0]);
  const [inMemoryOf, setInMemoryOf] = useState("");
  const [notes, setNotes] = useState("");
  const [everyYear, setEveryYear] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [headcountError, setHeadcountError] = useState<string | null>(null);

  const people = Number(headcount);
  const validPeople = /^\d+$/.test(headcount.trim()) && people >= slot.minHeadcount && people <= max;
  const cost = slot.estimatedCostPerHead !== null && validPeople ? slot.estimatedCostPerHead * people : null;

  function step(by: number) {
    const current = /^\d+$/.test(headcount.trim()) ? people : slot.minHeadcount;
    setHeadcount(String(Math.min(max, Math.max(slot.minHeadcount, current + by))));
    setHeadcountError(null);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    if (!validPeople) {
      setHeadcountError(t("headcountRange", { min: slot.minHeadcount, max }));
      return;
    }

    setSubmitting(true);
    const result = await book({
      mealSlot: slot.publicId,
      date: day.date,
      headcount: people,
      fulfilment,
      inMemoryOf: inMemoryOf.trim() || null,
      notes: notes.trim() || null,
      everyYear,
      memorialPlan: memorial?.plan ?? null,
      memorialRule: memorial?.rule ?? null,
    });
    setSubmitting(false);

    if (result.ok) {
      onBooked(result.value);
      return;
    }
    if (result.failure.code && dayGone.has(result.failure.code) && result.failure.code !== "already_booked") {
      onTaken();
      return;
    }
    setFailure(result.failure);
  }

  return (
    <form onSubmit={submit} noValidate className="contents">
      <SheetBody className="grid gap-5">
        {memorial && <p className="rounded-cup bg-accent/12 px-4 py-3 text-sm text-fg">{t("forMemorial")}</p>}

        <Field
          label={t("headcount")}
          description={t("headcountHint", { min: slot.minHeadcount, max })}
          error={headcountError}
        >
          <Stepper value={headcount} onChange={setHeadcount} onStep={step} min={slot.minHeadcount} max={max} />
        </Field>

        {cost !== null && (
          <p className="-mt-2 rounded-cup bg-subtle px-4 py-3 text-sm text-fg">
            {t("estimate", {
              cost: format.number(cost, { style: "currency", currency: "LKR", maximumFractionDigits: 0 }),
            })}
          </p>
        )}

        <fieldset className="grid gap-2">
          <legend className="mb-2 text-base font-medium text-fg">{t("whoBrings")}</legend>
          {slot.allowedFulfilment.map((mode) => (
            <label
              key={mode}
              className={cn(
                "flex min-h-14 cursor-pointer items-start gap-3 rounded-cup border-[1.5px] bg-surface px-4 py-3",
                "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                fulfilment === mode ? "border-brand bg-brand/6" : "border-hairline",
              )}
            >
              <input
                type="radio"
                name="fulfilment"
                value={mode}
                checked={fulfilment === mode}
                onChange={() => setFulfilment(mode)}
                className="mt-1 size-5 accent-[var(--color-brand)]"
              />
              <span className="grid gap-0.5">
                <span className="font-medium text-fg">{booking(`fulfilment.${mode}`)}</span>
                <span className="text-sm text-muted">{t(`fulfilmentHint.${mode}`)}</span>
              </span>
            </label>
          ))}
        </fieldset>

        <Field label={t("inMemoryOf")} description={t("inMemoryOfHint")}>
          <Input value={inMemoryOf} onChange={(event) => setInMemoryOf(event.target.value)} maxLength={MAX_IN_MEMORY_OF} autoComplete="off" />
        </Field>

        <Field label={t("notes")} description={t("notesHint")}>
          <textarea
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
            maxLength={MAX_NOTES}
            rows={3}
            className={cn(inputClasses, "resize-y")}
          />
        </Field>

        <div className="grid gap-1">
          <Switch checked={everyYear} onChange={setEveryYear}>
            {t("everyYear")}
          </Switch>
          <p className="px-1 text-sm text-muted">{t("everyYearHint")}</p>
        </div>

        <FormError>
          {failure &&
            (failure.fields.includes("everyYear") ? t("everyYearNotYearly") : errorMessage(failure))}
        </FormError>

        <p className="text-sm text-muted">{slot.requiresApproval ? t("approvalNote") : t("instantNote")}</p>
      </SheetBody>

      <SheetFooter>
        <Button type="submit" size="lg" loading={submitting} className="w-full sm:w-auto">
          {slot.requiresApproval ? t("request") : t("confirm")}
        </Button>
      </SheetFooter>
    </form>
  );
}

/** − 25 + : the people, by tens of taps or by typing. */
function Stepper({
  value,
  onChange,
  onStep,
  min,
  max,
  ...aria
}: {
  value: string;
  onChange: (value: string) => void;
  onStep: (by: number) => void;
  min: number;
  max: number;
  id?: string;
  "aria-describedby"?: string;
  "aria-invalid"?: boolean;
}) {
  const t = useTranslations("Discover.sheet");
  const number = Number(value);
  return (
    <div className="flex items-stretch gap-2">
      <Button variant="outline" size="icon" onClick={() => onStep(-1)} disabled={number <= min} aria-label={t("fewer")}>
        <span aria-hidden className="text-xl leading-none">−</span>
      </Button>
      <Input
        {...aria}
        inputMode="numeric"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="w-24 text-center text-lg tabular-nums"
      />
      <Button variant="outline" size="icon" onClick={() => onStep(1)} disabled={number >= max} aria-label={t("more")}>
        <span aria-hidden className="text-xl leading-none">+</span>
      </Button>
    </div>
  );
}

/** The booking made: its number, and what happens next. */
function Done({ bookingNo, status, memorial, onClose }: { bookingNo: string; status: BookingStatus; memorial: boolean; onClose: () => void }) {
  const t = useTranslations("Discover.sheet");
  const confirmed = status === "Approved";

  return (
    <>
      <SheetBody className="grid justify-items-center gap-3 py-6 text-center" aria-live="polite">
        <CheckCircleIcon className="size-12 text-success" />
        <p className="text-2xl font-semibold text-brand">{confirmed ? t("doneConfirmed") : t("doneRequested")}</p>
        <p className="text-sm text-muted">{t("bookingNo")}</p>
        <p className="rounded-full bg-subtle px-4 py-1.5 font-medium tabular-nums tracking-wide text-fg">{bookingNo}</p>
        <p className="max-w-sm text-fg">{confirmed ? t("doneConfirmedNext") : t("doneRequestedNext")}</p>
        <p className="font-tagline text-lg italic text-muted">{t("thanks")}</p>
      </SheetBody>
      <SheetFooter className="justify-center">
        <Button asChild>
          <Link href={memorial ? "/memorial" : "/bookings/mine"}>{memorial ? t("seePlan") : t("seeMine")}</Link>
        </Button>
        <Button variant="ghost" onClick={onClose}>
          {t("close")}
        </Button>
      </SheetFooter>
    </>
  );
}

