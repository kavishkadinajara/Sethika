"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import type { BookingFailure } from "@/lib/bookings/api";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { declineDay, giveDay } from "@/lib/sponsors/api";
import type { InstitutionDayRequest } from "@/lib/sponsors/types";
import { cn, focusRing } from "@/lib/utils";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";

const MAX_REASON = 300;

/** Answers that mean the request or its day moved on while staff looked: the tray is read again. */
const moved = new Set(["day_not_held", "day_request_answered", "day_request_not_found", "stale"]);

/**
 * Donors asking for a day a regular donor holds (plan 08, K6), beside the booking requests. Giving it
 * lets the regular donor's day go and books the asker, approved (K27); keeping it texts the asker.
 */
export function DayRequestTray({ requests, onAnswered }: { requests: InstitutionDayRequest[]; onAnswered: () => void }) {
  const t = useTranslations("Calendar.dayRequests");
  if (requests.length === 0) return null;

  return (
    <section className="grid gap-3" aria-labelledby="day-requests">
      <div className="flex flex-wrap items-center gap-3">
        <h2 id="day-requests" className="text-xl font-semibold text-brand">
          {t("heading")}
        </h2>
        <span className="rounded-full bg-accent px-3 py-0.5 text-sm font-semibold tabular-nums text-on-accent">{requests.length}</span>
      </div>
      <p className="text-sm text-muted">{t("intro")}</p>
      <ul className="grid gap-3 md:grid-cols-2 2xl:grid-cols-3">
        {requests.map((request) => (
          <li key={request.requestNo}>
            <DayRequestItem request={request} onAnswered={onAnswered} />
          </li>
        ))}
      </ul>
    </section>
  );
}

function DayRequestItem({ request, onAnswered }: { request: InstitutionDayRequest; onAnswered: () => void }) {
  const t = useTranslations("Calendar.dayRequests");
  const people = useTranslations("Calendar.booking");
  const fulfilment = useTranslations("Booking.fulfilment");
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useBookingError();
  const { can } = usePermissions();
  const [whose, setWhose] = useState(request.holders[0]?.sponsorship ?? "");
  const [declining, setDeclining] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState<"give" | "decline" | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  function failed(result: BookingFailure) {
    setFailure(result);
    if (result.code && moved.has(result.code)) onAnswered();
  }

  async function give() {
    setBusy("give");
    setFailure(null);
    const result = await giveDay(request.requestNo, request.holders.length > 1 ? whose : null);
    setBusy(null);
    if (!result.ok) return failed(result.failure);
    toast({ variant: "success", title: t("given", { requestNo: request.requestNo, name: request.donorName }) });
    onAnswered();
  }

  async function decline(event: FormEvent) {
    event.preventDefault();
    setBusy("decline");
    setFailure(null);
    const result = await declineDay(request.requestNo, reason.trim() || null);
    setBusy(null);
    if (!result.ok) return failed(result.failure);
    toast({ variant: "success", title: t("declined", { requestNo: request.requestNo, name: request.donorName }) });
    onAnswered();
  }

  return (
    <article className="grid gap-2 rounded-cup border border-accent/50 bg-surface p-4" aria-label={t("asks", { name: request.donorName })}>
      <p className="text-sm font-medium text-brand">
        {dates.dayMonth(request.date)} · {request.mealSlotName}
      </p>
      <div className="grid min-w-0 gap-0.5">
        <p className="font-semibold text-fg">{t("asks", { name: request.donorName })}</p>
        {request.donorPhone && (
          <a href={`tel:${request.donorPhone}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1 tabular-nums")}>
            {request.donorPhone}
          </a>
        )}
      </div>
      <p className="text-sm text-muted">
        {people("people", { count: request.headcount })} · {fulfilment(request.fulfilment)} · {request.requestNo}
      </p>
      {request.note && <p className="rounded-cup bg-subtle px-3 py-2 text-sm text-fg">{request.note}</p>}

      {request.holders.length === 1 ? (
        <p className="text-sm text-fg">{t("heldBy", { name: request.holders[0].donorName })}</p>
      ) : (
        request.holders.length > 1 && (
          <Field label={t("whose")}>
            <select value={whose} onChange={(e) => setWhose(e.target.value)} className={inputClasses}>
              {request.holders.map((h) => (
                <option key={h.sponsorship} value={h.sponsorship}>
                  {h.donorName}
                </option>
              ))}
            </select>
          </Field>
        )
      )}

      {declining ? (
        <form onSubmit={decline} className="grid gap-3 pt-1">
          <Field label={t("declineReason")} description={t("declineHint")}>
            <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={MAX_REASON} rows={2} autoFocus className={cn(inputClasses, "resize-y")} />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="danger" loading={busy === "decline"}>
              {t("confirmDecline")}
            </Button>
            <Button variant="ghost" onClick={() => setDeclining(false)}>
              {t("back")}
            </Button>
          </div>
        </form>
      ) : (
        <div className="flex flex-wrap gap-2 pt-1">
          {can("BOOKINGS.CALENDAR.APPROVE") && (
            <Button variant="outline" loading={busy === "give"} onClick={() => void give()}>
              {t("give")}
            </Button>
          )}
          {can("BOOKINGS.CALENDAR.REJECT") && (
            <Button variant="ghost" onClick={() => setDeclining(true)}>
              {t("decline")}
            </Button>
          )}
        </div>
      )}
      <FormError>{failure && errorMessage(failure)}</FormError>
    </article>
  );
}
