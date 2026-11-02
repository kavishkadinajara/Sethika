"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { CheckCircleIcon } from "@/components/ui/icons";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { localName } from "@/lib/bookings/names";
import type { FulfilmentMode, PublicInstitution, PublicMealSlot } from "@/lib/bookings/types";
import { askForDay } from "@/lib/sponsors/api";
import { cn } from "@/lib/utils";
import { DonorSheetGate } from "./DonorSheetGate";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";
import { useWide } from "./useWide";

const MAX_NOTE = 500;

type AskDaySheetProps = {
  institution: PublicInstitution;
  slot: PublicMealSlot;
  date: string;
  onClose: () => void;
  /** The day is no longer held (given, let go, opened): close and show the month as it is now. */
  onMoved: () => void;
};

/**
 * Asking for a day a regular donor holds (plan 08, K6, K26). Asking holds nothing: the institution
 * decides, and the donor is texted either way. The headcount is the slot's, since the day's places are
 * the regular donor's until it is given.
 */
export function AskDaySheet({ institution, slot, date, onClose, onMoved }: AskDaySheetProps) {
  const t = useTranslations("Discover.ask");
  const locale = useLocale();
  const wide = useWide();
  const dates = useDates();
  const [done, setDone] = useState<string | null>(null);

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <SheetHeader>
          <SheetTitle>{t("title", { date: dates.full(date) })}</SheetTitle>
          <SheetDescription>
            {localName(locale, slot)} · {slot.startTime}–{slot.endTime} · {localName(locale, institution)}
          </SheetDescription>
        </SheetHeader>

        {done ? (
          <>
            <SheetBody className="grid justify-items-center gap-3 py-6 text-center">
              <CheckCircleIcon className="size-12 text-brand" />
              <p role="status" className="text-lg font-semibold text-brand">
                {t("done", { requestNo: done })}
              </p>
              <p className="text-fg">{t("doneNext")}</p>
            </SheetBody>
            <SheetFooter>
              <Button asChild variant="outline">
                <Link href="/bookings/mine">{t("myBookings")}</Link>
              </Button>
              <Button onClick={onClose}>{t("close")}</Button>
            </SheetFooter>
          </>
        ) : (
          <DonorSheetGate
            back={`/i/${institution.slug}?slot=${encodeURIComponent(slot.publicId)}&date=${date}`}
            permission="MYBOOKINGS.BOOKINGS.CREATE"
            signInFirst={t("signInFirst")}
            institutionSession={t("institutionSession")}
            notAllowed={t("notAllowed")}
          >
            <AskForm slug={institution.slug} slot={slot} date={date} onAsked={setDone} onMoved={onMoved} onClose={onClose} />
          </DonorSheetGate>
        )}
      </SheetContent>
    </Sheet>
  );
}

function AskForm({ slug, slot, date, onAsked, onMoved, onClose }: { slug: string; slot: PublicMealSlot; date: string; onAsked: (requestNo: string) => void; onMoved: () => void; onClose: () => void }) {
  const t = useTranslations("Discover.ask");
  const booking = useTranslations("Booking");
  const errorMessage = useBookingError();
  const [headcount, setHeadcount] = useState(String(slot.minHeadcount));
  const [fulfilment, setFulfilment] = useState<FulfilmentMode>(slot.allowedFulfilment[0]);
  const [note, setNote] = useState("");
  const [headcountError, setHeadcountError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    const people = Number(headcount);
    if (!/^\d+$/.test(headcount.trim()) || people < slot.minHeadcount || people > slot.maxHeadcount) {
      setHeadcountError(t("headcountRange", { min: slot.minHeadcount, max: slot.maxHeadcount }));
      return;
    }
    setHeadcountError(null);

    setSubmitting(true);
    const result = await askForDay(slug, { mealSlot: slot.publicId, date, headcount: people, fulfilment, note: note.trim() || null });
    setSubmitting(false);

    if (result.ok) return onAsked(result.value.requestNo);
    if (result.failure.code === "day_not_held") return onMoved();
    setFailure(result.failure);
  }

  return (
    <form onSubmit={submit} noValidate className="contents">
      <SheetBody className="grid gap-5">
        <p className="rounded-cup bg-accent/12 px-4 py-3 text-sm text-fg">{t("explain")}</p>
        <Field label={t("headcount")} description={t("headcountHint", { min: slot.minHeadcount, max: slot.maxHeadcount })} error={headcountError} required>
          <Input inputMode="numeric" value={headcount} onChange={(e) => setHeadcount(e.target.value)} className="w-28" />
        </Field>
        {slot.allowedFulfilment.length > 1 && (
          <fieldset className="grid gap-2">
            <legend className="mb-1 text-base font-medium text-fg">{t("fulfilment")}</legend>
            {slot.allowedFulfilment.map((mode) => (
              <label key={mode} className="flex min-h-11 cursor-pointer items-center gap-3 text-fg">
                <input type="radio" name="askFulfilment" checked={fulfilment === mode} onChange={() => setFulfilment(mode)} className="size-5 accent-[var(--color-brand)]" />
                {booking(`fulfilment.${mode}`)}
              </label>
            ))}
          </fieldset>
        )}
        <Field label={t("note")} description={t("noteHint")}>
          <textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={MAX_NOTE} rows={3} className={cn(inputClasses, "resize-y")} />
        </Field>
        <FormError>{failure && errorMessage(failure)}</FormError>
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" onClick={onClose}>
          {t("cancel")}
        </Button>
        <Button type="submit" loading={submitting}>
          {t("send")}
        </Button>
      </SheetFooter>
    </form>
  );
}
