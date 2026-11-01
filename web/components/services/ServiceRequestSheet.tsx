"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { BowlCalendar } from "@/components/bookings/BowlCalendar";
import { DonorSheetGate } from "@/components/bookings/DonorSheetGate";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { CheckCircleIcon } from "@/components/ui/icons";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { colomboToday, monthOf } from "@/lib/bookings/calendar";
import { localName } from "@/lib/bookings/names";
import type { PublicInstitution } from "@/lib/bookings/types";
import { requestService } from "@/lib/services/api";
import { endTime, requestWindow, startTimes } from "@/lib/services/time";
import type { PublicServiceType, ServiceRequest } from "@/lib/services/types";
import { cn } from "@/lib/utils";
import { useDuration } from "./useDuration";
import { useServiceError } from "./useServiceError";

// The columns' lengths in booking.ServiceAppointments.
const MAX_ADDRESS = 300;
const MAX_OCCASION = 200;
const MAX_NOTE = 500;

type Place = ServiceRequest["location"];

/**
 * Asking an institution for a religious service (Phase E, section 4, flow 1): a day inside the
 * service's advance window, a start time on the 15-minute grid (V11), where, and for what. A bottom
 * sheet on a phone, a side sheet on a wider screen. Nothing is held yet: the institution answers
 * by assigning its leaders, and the donor is texted.
 */
export function ServiceRequestSheet({ institution, service, onClose }: { institution: PublicInstitution; service: PublicServiceType; onClose: () => void }) {
  const t = useTranslations("Services.sheet");
  const locale = useLocale();
  const wide = useWide();
  const duration = useDuration();
  const [done, setDone] = useState<string | null>(null);

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <SheetHeader>
          <SheetTitle>{localName(locale, service)}</SheetTitle>
          <SheetDescription>
            {localName(locale, institution)} · {duration(service.durationMinutes)}
          </SheetDescription>
        </SheetHeader>

        {done ? (
          <Done appointmentNo={done} onClose={onClose} />
        ) : (
          <DonorSheetGate
            back={`/i/${institution.slug}?service=${encodeURIComponent(service.publicId)}`}
            permission="MYSERVICES.APPOINTMENTS.CREATE"
            signInFirst={t("signInFirst")}
            institutionSession={t("institutionSession")}
            notAllowed={t("notAllowed")}
          >
            <RequestForm institution={institution} service={service} onRequested={setDone} />
          </DonorSheetGate>
        )}
      </SheetContent>
    </Sheet>
  );
}

function RequestForm({ institution, service, onRequested }: { institution: PublicInstitution; service: PublicServiceType; onRequested: (appointmentNo: string) => void }) {
  const t = useTranslations("Services.sheet");
  const offered = useTranslations("Services.offered");
  const locale = useLocale();
  const dates = useDates();
  const errorMessage = useServiceError();

  const span = requestWindow(colomboToday(), service.minAdvanceDays, service.maxAdvanceDays);
  const places: Place[] = service.locationMode === "Either" ? ["AtInstitution", "AtDonorPlace"] : [service.locationMode];

  const [month, setMonth] = useState(monthOf(span.first));
  const [date, setDate] = useState<string | null>(null);
  const [time, setTime] = useState("09:00");
  const [place, setPlace] = useState<Place>(places[0]);
  const [address, setAddress] = useState("");
  const [occasion, setOccasion] = useState("");
  const [note, setNote] = useState("");
  const [dateError, setDateError] = useState<string | null>(null);
  const [addressError, setAddressError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    const missingDate = date === null;
    const missingAddress = place === "AtDonorPlace" && address.trim() === "";
    setDateError(missingDate ? t("dateRequired") : null);
    setAddressError(missingAddress ? t("addressRequired") : null);
    if (missingDate || missingAddress) return;

    setSubmitting(true);
    const result = await requestService({
      serviceType: service.publicId,
      date: date!,
      startTime: time,
      location: place,
      address: place === "AtDonorPlace" ? address.trim() : null,
      occasion: occasion.trim() || null,
      note: note.trim() || null,
    });
    setSubmitting(false);

    if (result.ok) onRequested(result.value.appointmentNo);
    else setFailure(result.failure);
  }

  return (
    <form onSubmit={submit} noValidate className="contents">
      <SheetBody className="grid gap-5">
        <fieldset className="grid gap-2">
          <legend className="mb-1 text-base font-medium text-fg">{t("date")}</legend>
          <p className="text-sm text-muted">{t("dateHint")}</p>
          <BowlCalendar
            month={month}
            onMonthChange={setMonth}
            canGoBack={month > monthOf(span.first)}
            canGoForward={month < monthOf(span.last)}
            selected={date}
            onSelect={(day) => {
              setDate(day);
              setDateError(null);
            }}
            day={(day) => {
              const open = day >= span.first && day <= span.last;
              return { label: open ? dates.full(day) : `${dates.full(day)}. ${t("notOffered")}`, disabled: !open };
            }}
          />
          {date && <p className="text-sm font-medium text-fg">{dates.full(date)}</p>}
          {dateError && <p className="text-sm font-medium text-danger">{dateError}</p>}
        </fieldset>

        <Field label={t("time")} description={t("timeHint", { end: endTime(time, service.durationMinutes) })}>
          <select value={time} onChange={(event) => setTime(event.target.value)} className={cn(inputClasses, "w-40 tabular-nums")}>
            {startTimes().map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </Field>

        {places.length > 1 && (
          <fieldset className="grid gap-2">
            <legend className="mb-2 text-base font-medium text-fg">{t("where")}</legend>
            {places.map((option) => (
              <label
                key={option}
                className={cn(
                  "flex min-h-14 cursor-pointer items-center gap-3 rounded-cup border-[1.5px] bg-surface px-4 py-3",
                  "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                  place === option ? "border-brand bg-brand/6" : "border-hairline",
                )}
              >
                <input
                  type="radio"
                  name="place"
                  value={option}
                  checked={place === option}
                  onChange={() => setPlace(option)}
                  className="size-5 accent-[var(--color-brand)]"
                />
                <span className="font-medium text-fg">
                  {option === "AtInstitution" ? t("atInstitution", { institution: localName(locale, institution) }) : t("atHome")}
                </span>
              </label>
            ))}
          </fieldset>
        )}
        {places.length === 1 && <p className="text-sm text-muted">{offered(`where.${places[0]}`)}</p>}

        {place === "AtDonorPlace" && (
          <Field label={t("address")} description={t("addressHint")} error={addressError}>
            <textarea
              value={address}
              onChange={(event) => {
                setAddress(event.target.value);
                setAddressError(null);
              }}
              maxLength={MAX_ADDRESS}
              rows={2}
              autoComplete="street-address"
              className={cn(inputClasses, "resize-y")}
            />
          </Field>
        )}

        <Field label={t("occasion")} description={t("occasionHint")}>
          <Input value={occasion} onChange={(event) => setOccasion(event.target.value)} maxLength={MAX_OCCASION} autoComplete="off" />
        </Field>

        <Field label={t("note")} description={t("noteHint")}>
          <textarea value={note} onChange={(event) => setNote(event.target.value)} maxLength={MAX_NOTE} rows={3} className={cn(inputClasses, "resize-y")} />
        </Field>

        <FormError>{failure && errorMessage(failure)}</FormError>

        <p className="text-sm text-muted">{t("approvalNote")}</p>
      </SheetBody>

      <SheetFooter>
        <Button type="submit" size="lg" loading={submitting} className="w-full sm:w-auto">
          {t("submit")}
        </Button>
      </SheetFooter>
    </form>
  );
}

/** The request made: its number, and what happens next. */
function Done({ appointmentNo, onClose }: { appointmentNo: string; onClose: () => void }) {
  const t = useTranslations("Services.sheet");
  return (
    <>
      <SheetBody className="grid justify-items-center gap-3 py-6 text-center" aria-live="polite">
        <CheckCircleIcon className="size-12 text-success" />
        <p className="text-2xl font-semibold text-brand">{t("done")}</p>
        <p className="text-sm text-muted">{t("appointmentNo")}</p>
        <p className="rounded-full bg-subtle px-4 py-1.5 font-medium tabular-nums tracking-wide text-fg">{appointmentNo}</p>
        <p className="max-w-sm text-fg">{t("doneNext")}</p>
      </SheetBody>
      <SheetFooter className="justify-center">
        <Button asChild>
          <Link href="/services/mine">{t("seeMine")}</Link>
        </Button>
        <Button variant="ghost" onClick={onClose}>
          {t("close")}
        </Button>
      </SheetFooter>
    </>
  );
}
