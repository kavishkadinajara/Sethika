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
import { getSchedule, respondAsLeader } from "@/lib/services/api";
import { colomboParts } from "@/lib/services/time";
import type { ScheduleItem } from "@/lib/services/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { useServiceError } from "./useServiceError";

const MAX_REASON = 300;

/**
 * A religious leader's own schedule (Phase E, promise 4): the services they are assigned to that
 * are still to come, soonest first, and their answer to each. Declining frees their time and
 * tells the institution it needs someone else (V6).
 */
export function ScheduleScreen() {
  const t = useTranslations("Services.schedule");
  const locale = useLocale();
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useServiceError();
  const { can } = usePermissions();
  const canAnswer = can("SERVICES.MYSCHEDULE.EDIT");
  const [items, setItems] = useState<ScheduleItem[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [declining, setDeclining] = useState<ScheduleItem | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await getSchedule();
    if (result.ok) setItems(result.value);
    else setFailure(result.failure);
  }, []);

  useEffect(() => {
    // Loading the schedule is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  async function accept(item: ScheduleItem) {
    setBusy(item.appointmentNo);
    const result = await respondAsLeader(item.appointmentNo, true, null);
    setBusy(null);
    if (result.ok) toast({ variant: "success", title: t("acceptedToast", { appointmentNo: item.appointmentNo }) });
    else toast({ variant: "error", title: errorMessage(result.failure) });
    void load();
  }

  if (failure && !items) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!items) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  if (items.length === 0) return <EmptyState>{t("none")}</EmptyState>;

  return (
    <>
      <ul className="grid gap-3 xl:grid-cols-2">
        {items.map((item) => {
          const start = colomboParts(item.startsAt);
          const end = colomboParts(item.endsAt);
          return (
            <li key={item.appointmentNo}>
              <article className="grid gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <h2 className="min-w-0 text-lg font-semibold leading-snug text-fg">{inLocale(locale, item.serviceName, item.serviceNameSi, item.serviceNameTa)}</h2>
                  <span
                    className={cn(
                      "rounded-full border px-3 py-1 text-sm font-medium",
                      item.response === "Accepted" ? "border-brand bg-primary text-on-primary" : "border-accent/60 bg-accent/15 text-fg",
                    )}
                  >
                    {t(`response.${item.response}`)}
                  </span>
                </div>
                <p className="text-base text-brand">
                  {dates.full(start.date)} · <span className="tabular-nums">{start.time}–{end.time}</span>
                </p>
                <p className="text-sm text-muted">{item.locationMode === "AtDonorPlace" ? t("atDonor", { address: item.address ?? "" }) : t("atInstitution")}</p>
                <p className="text-sm text-fg">
                  {item.donorName} ·{" "}
                  <a href={`tel:${item.donorPhone}`} className={cn(textLink, focusRing, "min-h-0")}>
                    {item.donorPhone}
                  </a>
                </p>
                {item.occasion && <p className="font-tagline text-base italic text-fg">{item.occasion}</p>}
                {canAnswer && (
                  <div className="flex flex-wrap gap-2 pt-1">
                    {item.response === "Pending" && (
                      <Button loading={busy === item.appointmentNo} onClick={() => void accept(item)}>
                        {t("accept")}
                      </Button>
                    )}
                    <Button variant="ghost" className="text-danger hover:bg-danger/10" onClick={() => setDeclining(item)}>
                      {t("decline")}
                    </Button>
                  </div>
                )}
              </article>
            </li>
          );
        })}
      </ul>

      {declining && (
        <DeclineSheet
          item={declining}
          onClose={() => setDeclining(null)}
          onDeclined={() => {
            setDeclining(null);
            toast({ variant: "success", title: t("declinedToast", { appointmentNo: declining.appointmentNo }) });
            void load();
          }}
        />
      )}
    </>
  );
}

function DeclineSheet({ item, onClose, onDeclined }: { item: ScheduleItem; onClose: () => void; onDeclined: () => void }) {
  const t = useTranslations("Services.schedule.declineSheet");
  const wide = useWide();
  const errorMessage = useServiceError();
  const [reason, setReason] = useState("");
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setFailure(null);
    const result = await respondAsLeader(item.appointmentNo, false, reason.trim() || null);
    setSaving(false);
    if (result.ok) onDeclined();
    else setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <form onSubmit={submit} className="contents">
          <SheetHeader>
            <SheetTitle>{t("title")}</SheetTitle>
            <SheetDescription>{t("description", { appointmentNo: item.appointmentNo })}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-4">
            <Field label={t("reason")} description={t("reasonHint")}>
              <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={MAX_REASON} rows={3} className={cn(inputClasses, "resize-y")} />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("keep")}
            </Button>
            <Button type="submit" variant="danger" loading={saving}>
              {t("confirm")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
