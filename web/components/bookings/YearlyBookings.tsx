"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import { stopYearly, type BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import type { YearlyBooking } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";
import { useWide } from "./useWide";

/**
 * "Every year" in My bookings (plan 07, H5): the donor's yearly bookings, the day each one books
 * next, and a stop. Stopping keeps a year already booked; that booking is cancelled the usual way.
 */
export function YearlyBookings({ yearly, onStopped }: { yearly: YearlyBooking[]; onStopped: () => void }) {
  const t = useTranslations("MyBookings.yearly");
  const [stopping, setStopping] = useState<YearlyBooking | null>(null);

  return (
    <section className="grid gap-3" aria-labelledby="every-year">
      <div className="grid gap-1">
        <h2 id="every-year" className="text-xl font-semibold text-brand">
          {t("title")}
        </h2>
        <p className="text-sm text-muted">{t("intro")}</p>
      </div>
      <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
        {yearly.map((y) => (
          <li key={y.publicId}>
            <YearlyCard yearly={y} onStop={() => setStopping(y)} />
          </li>
        ))}
      </ul>
      {stopping && (
        <StopSheet
          yearly={stopping}
          onClose={() => setStopping(null)}
          onStopped={() => {
            setStopping(null);
            onStopped();
          }}
        />
      )}
    </section>
  );
}

function YearlyCard({ yearly, onStop }: { yearly: YearlyBooking; onStop: () => void }) {
  const t = useTranslations("MyBookings.yearly");
  const people = useTranslations("MyBookings");
  const locale = useLocale();
  const dates = useDates();
  // The day and month, in a year that has 29 February, so a leap-day booking reads as itself.
  const dayMonth = dates.dayMonth(`2028-${String(yearly.month).padStart(2, "0")}-${String(yearly.day).padStart(2, "0")}`);

  return (
    <article className="grid gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
      <p className="text-sm font-medium text-brand">{t("every", { day: dayMonth })}</p>
      <h3 className="text-lg font-semibold leading-snug text-fg">
        {inLocale(locale, yearly.slotName, yearly.slotNameSi, yearly.slotNameTa)}
        <span className="font-normal text-muted"> · {yearly.startTime}</span>
      </h3>
      <Link href={`/i/${yearly.institution}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1")}>
        {inLocale(locale, yearly.institutionName, yearly.institutionNameSi, yearly.institutionNameTa)}
      </Link>
      <p className="text-sm text-muted">
        {people("people", { count: yearly.headcount })} ·{" "}
        {yearly.nextDate ? t("next", { date: dates.full(yearly.nextDate) }) : t("ended")}
      </p>
      {yearly.inMemoryOf && <p className="font-tagline text-base italic text-fg">{people("inMemoryOf", { name: yearly.inMemoryOf })}</p>}
      <div className="pt-1">
        <Button variant="ghost" onClick={onStop} className="-ml-3 text-danger hover:bg-danger/10">
          {t("stop")}
        </Button>
      </div>
    </article>
  );
}

function StopSheet({ yearly, onClose, onStopped }: { yearly: YearlyBooking; onClose: () => void; onStopped: () => void }) {
  const t = useTranslations("MyBookings.yearly");
  const toast = useToast();
  const wide = useWide();
  const locale = useLocale();
  const errorMessage = useBookingError();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function stop() {
    setBusy(true);
    setFailure(null);
    const result = await stopYearly(yearly.publicId);
    setBusy(false);
    if (result.ok) {
      toast({ variant: "success", title: t("stopped") });
      onStopped();
    } else {
      setFailure(result.failure);
    }
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <SheetHeader>
          <SheetTitle>{t("stopTitle")}</SheetTitle>
          <SheetDescription>
            {inLocale(locale, yearly.slotName, yearly.slotNameSi, yearly.slotNameTa)} ·{" "}
            {inLocale(locale, yearly.institutionName, yearly.institutionNameSi, yearly.institutionNameTa)}
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="grid gap-4">
          <p className="text-fg">{t("stopExplain")}</p>
          <FormError>{failure && errorMessage(failure)}</FormError>
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose}>
            {t("keep")}
          </Button>
          <Button variant="danger" loading={busy} onClick={() => void stop()}>
            {t("stopConfirm")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
