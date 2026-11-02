"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import type { BookingFailure } from "@/lib/bookings/api";
import { colomboToday } from "@/lib/bookings/calendar";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { endSponsorship, getSponsorship, skipDay, swapDays } from "@/lib/sponsors/api";
import { canSkip, swapPartners, swappableDays } from "@/lib/sponsors/pattern";
import type { Sponsorship, SponsorshipDay, SponsorshipDetails } from "@/lib/sponsors/types";
import { cn, focusRing } from "@/lib/utils";
import { usePatternSummary } from "./PatternSummary";
import { useSponsorError } from "./useSponsorError";

const MAX_REASON = 300;

/** Day states that are still the sponsor's to give; the rest are let go or moved, and shown quieter. */
const live = new Set(["booked", "open", "upcoming", "MovedFrom"]);

type Mode = { kind: "days" } | { kind: "skip"; day: SponsorshipDay } | { kind: "end" } | { kind: "swap" };

/**
 * One regular arrangement (plan 08): its next days and where each stands, and what staff can do -
 * say the sponsor cannot give on a day (K5), end it (K23), or swap a day with another regular donor
 * of the same meal (K8). Each change reads the arrangement again, since the server decides.
 */
export function SponsorshipSheet({ publicId, all, onClose, onChanged }: { publicId: string; all: Sponsorship[]; onClose: () => void; onChanged: () => void }) {
  const t = useTranslations("Sponsors.detail");
  const wide = useWide();
  const errorMessage = useSponsorError();
  const summary = usePatternSummary();
  const { can } = usePermissions();
  const canEdit = can("BOOKINGS.SPONSORS.EDIT");

  const [details, setDetails] = useState<SponsorshipDetails | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [mode, setMode] = useState<Mode>({ kind: "days" });

  const load = useCallback(async () => {
    setFailure(null);
    const result = await getSponsorship(publicId);
    if (result.ok) setDetails(result.value);
    else setFailure(result.failure);
  }, [publicId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const changed = () => {
    setMode({ kind: "days" });
    void load();
    onChanged();
  };

  const sponsorship = details?.sponsorship;
  const active = sponsorship?.status === "Active";

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <SheetHeader>
          <SheetTitle>{sponsorship ? t("title", { name: sponsorship.donorName }) : t("loading")}</SheetTitle>
          {sponsorship && (
            <SheetDescription>
              {sponsorship.mealSlotName} · {summary(sponsorship)}
            </SheetDescription>
          )}
        </SheetHeader>

        {!details ? (
          <SheetBody>
            {failure ? (
              <FormError>
                {errorMessage(failure)}{" "}
                <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
                  {t("retry")}
                </button>
              </FormError>
            ) : (
              <div className="flex justify-center py-12 text-brand">
                <Spinner />
              </div>
            )}
          </SheetBody>
        ) : mode.kind === "skip" ? (
          <SkipForm sponsorship={details.sponsorship} day={mode.day} onBack={() => setMode({ kind: "days" })} onDone={changed} />
        ) : mode.kind === "end" ? (
          <EndForm sponsorship={details.sponsorship} onBack={() => setMode({ kind: "days" })} onDone={changed} onStale={() => void load()} />
        ) : mode.kind === "swap" ? (
          <SwapForm details={details} all={all} onBack={() => setMode({ kind: "days" })} onDone={changed} />
        ) : (
          <>
            <SheetBody className="grid gap-4">
              <Facts sponsorship={details.sponsorship} />
              <Days details={details} canEdit={canEdit && active} onSkip={(day) => setMode({ kind: "skip", day })} />
            </SheetBody>
            <SheetFooter>
              {canEdit && active && (
                <>
                  <Button variant="ghost" onClick={() => setMode({ kind: "end" })} className="text-danger hover:bg-danger/10">
                    {t("end")}
                  </Button>
                  <Button variant="outline" onClick={() => setMode({ kind: "swap" })}>
                    {t("swap")}
                  </Button>
                </>
              )}
              <Button onClick={onClose}>{t("close")}</Button>
            </SheetFooter>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}

function Facts({ sponsorship }: { sponsorship: Sponsorship }) {
  const t = useTranslations("Sponsors.detail");
  const booking = useTranslations("Booking");
  const dates = useDates();

  return (
    <div className="grid gap-1 text-sm">
      <p className="text-fg">
        {t("people", { count: sponsorship.headcount })} · {booking(`fulfilment.${sponsorship.fulfilment}`)}
      </p>
      {sponsorship.inMemoryOf && <p className="font-tagline italic text-fg">{t("inMemoryOf", { name: sponsorship.inMemoryOf })}</p>}
      {sponsorship.notes && <p className="rounded-cup bg-subtle px-3 py-2 text-fg">{sponsorship.notes}</p>}
      {sponsorship.status === "Ended" && sponsorship.endedAt && <p className="font-medium text-muted">{t("endedOn", { date: dates.medium(sponsorship.endedAt.slice(0, 10)) })}</p>}
    </div>
  );
}

function Days({ details, canEdit, onSkip }: { details: SponsorshipDetails; canEdit: boolean; onSkip: (day: SponsorshipDay) => void }) {
  const t = useTranslations("Sponsors.detail");
  const dates = useDates();
  const today = colomboToday();

  return (
    <section className="grid gap-2" aria-labelledby="sponsorship-days">
      <h3 id="sponsorship-days" className="font-semibold text-brand">
        {t("nextDays")}
      </h3>
      {details.days.length === 0 ? (
        <p className="text-sm text-muted">{t("noDays")}</p>
      ) : (
        <ul className="grid gap-2">
          {details.days.map((day) => (
            <li key={day.date} data-date={day.date} data-state={day.state} className={cn("grid gap-1 rounded-cup border border-hairline px-3 py-2", !live.has(day.state) && "bg-subtle/60")}>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-medium tabular-nums text-fg">{dates.dayMonth(day.date)} {day.date.slice(0, 4)}</span>
                <span className={cn("text-sm", live.has(day.state) ? "text-brand" : "text-muted")}>{t(`state.${day.state}`)}</span>
              </div>
              {(day.reason || day.bookingNo) && (
                <p className="text-sm text-muted">{[day.reason, day.bookingNo].filter(Boolean).join(" · ")}</p>
              )}
              {canEdit && canSkip(day, today) && (
                <div>
                  <Button variant="ghost" onClick={() => onSkip(day)} aria-label={t("skipNamed", { date: dates.dayMonth(day.date) })} className="min-h-9 px-2 text-sm">
                    {t("skip")}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function SkipForm({ sponsorship, day, onBack, onDone }: { sponsorship: Sponsorship; day: SponsorshipDay; onBack: () => void; onDone: () => void }) {
  const t = useTranslations("Sponsors.detail");
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useSponsorError();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const when = dates.dayMonth(day.date);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setFailure(null);
    const result = await skipDay(sponsorship.publicId, day.date, reason.trim() || null);
    setBusy(false);
    if (!result.ok) return setFailure(result.failure);
    toast({ variant: "success", title: t("skipped", { date: when }) });
    onDone();
  }

  return (
    <form onSubmit={submit} className="contents">
      <SheetBody className="grid gap-4">
        <h3 className="text-lg font-semibold text-brand">{t("skipTitle", { date: when })}</h3>
        <p className="text-fg">{t("skipExplain")}</p>
        <Field label={t("reason")} description={t("reasonHint")}>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={MAX_REASON} rows={2} className={cn(inputClasses, "resize-y")} />
        </Field>
        <FormError>{failure && errorMessage(failure)}</FormError>
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" onClick={onBack}>
          {t("back")}
        </Button>
        <Button type="submit" variant="danger" loading={busy}>
          {t("skipConfirm")}
        </Button>
      </SheetFooter>
    </form>
  );
}

function EndForm({ sponsorship, onBack, onDone, onStale }: { sponsorship: Sponsorship; onBack: () => void; onDone: () => void; onStale: () => void }) {
  const t = useTranslations("Sponsors.detail");
  const toast = useToast();
  const errorMessage = useSponsorError();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setFailure(null);
    const result = await endSponsorship(sponsorship.publicId, sponsorship.version, reason.trim() || null);
    setBusy(false);
    if (!result.ok) {
      setFailure(result.failure);
      if (result.failure.code === "stale") onStale();
      return;
    }
    toast({ variant: "success", title: t("ended") });
    onDone();
  }

  return (
    <form onSubmit={submit} className="contents">
      <SheetBody className="grid gap-4">
        <h3 className="text-lg font-semibold text-brand">{t("endTitle")}</h3>
        <p className="text-fg">{t("endExplain")}</p>
        <Field label={t("reason")} description={t("endReasonHint")}>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={MAX_REASON} rows={2} className={cn(inputClasses, "resize-y")} />
        </Field>
        <FormError>{failure && errorMessage(failure)}</FormError>
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" onClick={onBack}>
          {t("back")}
        </Button>
        <Button type="submit" variant="danger" loading={busy}>
          {t("endConfirm")}
        </Button>
      </SheetFooter>
    </form>
  );
}

/**
 * Swapping a day with another regular donor of the same meal (K8, K29): this time only (both days
 * booked) or from now on (each takes on the other's days). The partner's days are read when chosen.
 */
function SwapForm({ details, all, onBack, onDone }: { details: SponsorshipDetails; all: Sponsorship[]; onBack: () => void; onDone: () => void }) {
  const t = useTranslations("Sponsors.swap");
  const dates = useDates();
  const toast = useToast();
  const summary = usePatternSummary();
  const errorMessage = useSponsorError();
  const today = colomboToday();
  const own = details.sponsorship;
  const partners = swapPartners(all, own);

  const [permanent, setPermanent] = useState(false);
  const [ownDate, setOwnDate] = useState("");
  const [partnerId, setPartnerId] = useState(partners[0]?.publicId ?? "");
  const [partner, setPartner] = useState<SponsorshipDetails | null>(null);
  const [partnerDate, setPartnerDate] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  useEffect(() => {
    if (!partnerId) return;
    let stale = false;
    void getSponsorship(partnerId).then((result) => {
      if (stale) return;
      setPartner(result.ok ? result.value : null);
      if (!result.ok) setFailure(result.failure);
    });
    return () => {
      stale = true;
    };
  }, [partnerId]);

  const ownDays = swappableDays(details.days, today, permanent);
  const partnerDays = partner && partner.sponsorship.publicId === partnerId ? swappableDays(partner.days, today, permanent) : null;
  const chosenOwn = ownDays.some((d) => d.date === ownDate) ? ownDate : "";
  const chosenPartner = partnerDays?.some((d) => d.date === partnerDate) ? partnerDate : "";

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!chosenOwn || !chosenPartner) return;
    setBusy(true);
    setFailure(null);
    const result = await swapDays({ sponsorship: own.publicId, date: chosenOwn }, { sponsorship: partnerId, date: chosenPartner }, permanent);
    setBusy(false);
    if (!result.ok) return setFailure(result.failure);
    toast({ variant: "success", title: t("done") });
    onDone();
  }

  const dayOptions = (days: SponsorshipDay[]) =>
    days.map((d) => (
      <option key={d.date} value={d.date}>
        {dates.dayMonth(d.date)} {d.date.slice(0, 4)}
      </option>
    ));

  return (
    <form onSubmit={submit} className="contents">
      <SheetBody className="grid gap-5">
        <h3 className="text-lg font-semibold text-brand">{t("title")}</h3>
        <p className="text-sm text-muted">{t("description")}</p>

        {partners.length === 0 ? (
          <p className="text-fg">{t("noPartner")}</p>
        ) : (
          <>
            <fieldset className="grid gap-2">
              <legend className="mb-1 text-base font-medium text-fg">{t("when")}</legend>
              {([false, true] as const).map((value) => (
                <label key={String(value)} className="flex min-h-11 cursor-pointer items-start gap-3 text-fg">
                  <input type="radio" name="swapWhen" checked={permanent === value} onChange={() => setPermanent(value)} className="mt-1 size-5 accent-[var(--color-brand)]" />
                  <span className="grid gap-0.5">
                    <span className="font-medium">{value ? t("permanent") : t("once")}</span>
                    <span className="text-sm text-muted">{value ? t("permanentHint") : t("onceHint")}</span>
                  </span>
                </label>
              ))}
            </fieldset>

            <Field label={t("own", { name: own.donorName })}>
              <select value={chosenOwn} onChange={(e) => setOwnDate(e.target.value)} className={inputClasses} required>
                <option value="">{t("choose")}</option>
                {dayOptions(ownDays)}
              </select>
            </Field>

            <Field label={t("partner")}>
              <select value={partnerId} onChange={(e) => setPartnerId(e.target.value)} className={inputClasses}>
                {partners.map((p) => (
                  <option key={p.publicId} value={p.publicId}>
                    {p.donorName} · {summary(p)}
                  </option>
                ))}
              </select>
            </Field>

            <Field label={t("partnerDay")}>
              {partnerDays === null ? (
                <div className="flex py-2 text-brand">
                  <Spinner />
                </div>
              ) : (
                <select value={chosenPartner} onChange={(e) => setPartnerDate(e.target.value)} className={inputClasses} required>
                  <option value="">{t("choose")}</option>
                  {dayOptions(partnerDays)}
                </select>
              )}
            </Field>
          </>
        )}
        <FormError>{failure && errorMessage(failure)}</FormError>
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" onClick={onBack}>
          {t("back")}
        </Button>
        {partners.length > 0 && (
          <Button type="submit" loading={busy} disabled={!chosenOwn || !chosenPartner}>
            {t("confirm")}
          </Button>
        )}
      </SheetFooter>
    </form>
  );
}
