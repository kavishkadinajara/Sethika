"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { PlusIcon } from "@/components/ui/icons";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import {
  archiveMemorialPlan,
  createMemorialPlan,
  listMemorialPlans,
  listMemorialReligions,
  listRelationships,
  type BookingFailure,
} from "@/lib/bookings/api";
import { colomboToday } from "@/lib/bookings/calendar";
import { inLocale, localName } from "@/lib/bookings/names";
import type { MemorialChoice, MemorialObservance, MemorialPlan, MemorialReligion } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";
import { EmptyState } from "./EmptyState";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";
import { useWide } from "./useWide";

/** MemorialPlan.MaxDeceasedName and MaxNotes. */
const MAX_NAME = 150;
const MAX_NOTES = 500;

/**
 * The Memorial Dana Planner (plan 07, H6): the people a donor remembers, and the days their faith
 * remembers them on - each booked, still to book, or gone by. "Find a place" opens the search on
 * that day; a booking made from there is the observance's. The days come from the faith's rules, which
 * are drafts until someone who knows confirms them (H7), and the page says so.
 */
export function MemorialPlanner() {
  const t = useTranslations("Memorial");
  const errorMessage = useBookingError();
  const [plans, setPlans] = useState<MemorialPlan[] | null>(null);
  const [religions, setReligions] = useState<MemorialReligion[]>([]);
  const [relationships, setRelationships] = useState<MemorialChoice[]>([]);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [adding, setAdding] = useState(false);

  const load = useCallback(async () => {
    setFailure(null);
    const [planList, religionList, relationshipList] = await Promise.all([listMemorialPlans(), listMemorialReligions(), listRelationships()]);
    if (religionList.ok) setReligions(religionList.value);
    if (relationshipList.ok) setRelationships(relationshipList.value);
    if (planList.ok) setPlans(planList.value);
    else setFailure(planList.failure);
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (failure) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (plans === null) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <div className="grid gap-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-prose rounded-cup bg-subtle px-4 py-3 text-sm text-fg">{t("draftNote")}</p>
        <Button onClick={() => setAdding(true)}>
          <PlusIcon className="size-5" />
          {t("add")}
        </Button>
      </div>

      {plans.length === 0 ? (
        <EmptyState>{t("none")}</EmptyState>
      ) : (
        <ul className="grid gap-6 xl:grid-cols-2">
          {plans.map((plan) => (
            <li key={plan.publicId}>
              <PlanCard plan={plan} religions={religions} relationships={relationships} onArchived={() => void load()} />
            </li>
          ))}
        </ul>
      )}

      {adding && (
        <PlanSheet
          religions={religions}
          relationships={relationships}
          onClose={() => setAdding(false)}
          onCreated={() => {
            setAdding(false);
            void load();
          }}
        />
      )}
    </div>
  );
}

function PlanCard({
  plan,
  religions,
  relationships,
  onArchived,
}: {
  plan: MemorialPlan;
  religions: MemorialReligion[];
  relationships: MemorialChoice[];
  onArchived: () => void;
}) {
  const t = useTranslations("Memorial");
  const locale = useLocale();
  const dates = useDates();
  const [archiving, setArchiving] = useState(false);
  const religion = religions.find((r) => r.code === plan.religion);
  const relationship = relationships.find((r) => r.code === plan.relationship);

  // What is still to come first, then what has gone by, latest first.
  const coming = plan.observances.filter((o) => o.state !== "past");
  const gone = plan.observances.filter((o) => o.state === "past").reverse();

  return (
    <article className="grid gap-4 rounded-bowl border border-hairline bg-surface p-5 shadow-lift">
      <header className="grid gap-1">
        <h2 className="font-tagline text-2xl italic text-brand">{plan.deceasedName}</h2>
        <p className="text-sm text-muted">
          {[relationship && localName(locale, relationship), religion && localName(locale, religion), t("passed", { date: dates.full(plan.dateOfPassing) })]
            .filter(Boolean)
            .join(" · ")}
        </p>
        {plan.notes && <p className="text-sm text-fg">{plan.notes}</p>}
      </header>

      {plan.observances.length === 0 ? (
        <p className="text-muted">{t("noObservances")}</p>
      ) : (
        <ol className="grid gap-2" aria-label={t("observances")}>
          {[...coming, ...gone].map((o) => (
            <li key={`${o.rule}-${o.date}`}>
              <ObservanceRow plan={plan} observance={o} />
            </li>
          ))}
        </ol>
      )}

      <div>
        <Button variant="ghost" onClick={() => setArchiving(true)} className="-ml-3 text-muted">
          {t("archive")}
        </Button>
      </div>
      {archiving && <ArchiveSheet plan={plan} onClose={() => setArchiving(false)} onArchived={onArchived} />}
    </article>
  );
}

function ObservanceRow({ plan, observance: o }: { plan: MemorialPlan; observance: MemorialObservance }) {
  const t = useTranslations("Memorial");
  const locale = useLocale();
  const dates = useDates();
  const name = localName(locale, o);
  const today = colomboToday();

  return (
    <div
      className={cn(
        "grid gap-1 rounded-cup border px-3 py-2.5",
        o.state === "booked" ? "border-brand/40 bg-brand/6" : o.state === "toBook" ? "border-accent/60 bg-accent/8" : "border-hairline bg-subtle",
      )}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <span className={cn("font-medium", o.state === "past" ? "text-muted" : "text-fg")}>
          {o.repeatsAnnually ? t("yearNumber", { name, number: o.number }) : name}
        </span>
        <span className="text-sm tabular-nums text-muted">{dates.full(o.date)}</span>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm text-fg">
          {t(`state.${o.state}`)}
          {o.state === "booked" && o.institution && (
            <>
              {" · "}
              <Link href={`/i/${o.institution}`} className={cn(textLink, focusRing, "min-h-0")}>
                {inLocale(locale, o.institutionName ?? o.institution, o.institutionNameSi, o.institutionNameTa)}
              </Link>
              {o.bookingNo && <span className="tabular-nums text-muted"> · {o.bookingNo}</span>}
            </>
          )}
          {o.everyYear && <span className="font-medium text-brand"> · {t("everyYear")}</span>}
        </span>
        {o.state === "toBook" && o.date >= today && (
          <Button asChild variant="outline">
            <Link
              href={`/find?date=${o.date}&plan=${encodeURIComponent(plan.publicId)}&rule=${encodeURIComponent(o.rule)}`}
              aria-label={t("findLabel", { name, date: dates.full(o.date) })}
            >
              {t("find")}
            </Link>
          </Button>
        )}
      </div>
      {o.state === "toBook" && o.repeatsAnnually && <p className="text-sm text-muted">{t("everyYearHint")}</p>}
    </div>
  );
}

function PlanSheet({
  religions,
  relationships,
  onClose,
  onCreated,
}: {
  religions: MemorialReligion[];
  relationships: MemorialChoice[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const t = useTranslations("Memorial.form");
  const locale = useLocale();
  const wide = useWide();
  const toast = useToast();
  const errorMessage = useBookingError();
  const [name, setName] = useState("");
  const [relationship, setRelationship] = useState("");
  const [dateOfPassing, setDateOfPassing] = useState("");
  const [religion, setReligion] = useState(religions[0]?.code ?? "");
  const [notes, setNotes] = useState("");
  const [fields, setFields] = useState<string[]>([]);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const today = colomboToday();
  const rules = religions.find((r) => r.code === religion)?.rules ?? [];

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    const missing = [!name.trim() && "deceasedName", !/^\d{4}-\d{2}-\d{2}$/.test(dateOfPassing) && "dateOfPassing", !religion && "religion"].filter(
      (f): f is string => Boolean(f),
    );
    setFields(missing);
    if (missing.length > 0) return;

    setSubmitting(true);
    const result = await createMemorialPlan({
      deceasedName: name.trim(),
      relationship: relationship || null,
      dateOfPassing,
      religion,
      notes: notes.trim() || null,
    });
    setSubmitting(false);
    if (result.ok) {
      toast({ variant: "success", title: t("created", { name: name.trim() }) });
      onCreated();
    } else {
      setFields(result.failure.fields);
      setFailure(result.failure);
    }
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
            <Field label={t("name")} error={fields.includes("deceasedName") ? t("nameRequired") : null} required>
              <Input value={name} onChange={(event) => setName(event.target.value)} maxLength={MAX_NAME} autoComplete="off" />
            </Field>
            <Field label={t("relationship")} error={fields.includes("relationship") ? t("invalid") : null}>
              <select value={relationship} onChange={(event) => setRelationship(event.target.value)} className={inputClasses}>
                <option value="">{t("relationshipNone")}</option>
                {relationships.map((r) => (
                  <option key={r.code} value={r.code}>
                    {localName(locale, r)}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t("dateOfPassing")} error={fields.includes("dateOfPassing") ? t("dateRequired") : null} required>
              <Input type="date" value={dateOfPassing} max={today} onChange={(event) => setDateOfPassing(event.target.value)} />
            </Field>
            <Field
              label={t("religion")}
              description={t("religionHint")}
              error={fields.includes("religion") ? t("religionRequired") : null}
              required
            >
              <select value={religion} onChange={(event) => setReligion(event.target.value)} className={inputClasses}>
                {religions.map((r) => (
                  <option key={r.code} value={r.code}>
                    {localName(locale, r)}
                  </option>
                ))}
              </select>
            </Field>
            {rules.length > 0 && (
              <div className="grid gap-1 rounded-cup bg-subtle px-4 py-3 text-sm">
                <p className="font-medium text-fg">{t("rulesTitle")}</p>
                <ul className="grid gap-0.5 text-muted">
                  {rules.map((rule) => (
                    <li key={rule.code}>{localName(locale, rule)}</li>
                  ))}
                </ul>
              </div>
            )}
            <Field label={t("notes")} description={t("notesHint")}>
              <textarea value={notes} onChange={(event) => setNotes(event.target.value)} maxLength={MAX_NOTES} rows={3} className={cn(inputClasses, "resize-y")} />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={submitting}>
              {t("save")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

function ArchiveSheet({ plan, onClose, onArchived }: { plan: MemorialPlan; onClose: () => void; onArchived: () => void }) {
  const t = useTranslations("Memorial.archiveSheet");
  const wide = useWide();
  const errorMessage = useBookingError();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function archive() {
    setBusy(true);
    setFailure(null);
    const result = await archiveMemorialPlan(plan.publicId);
    setBusy(false);
    if (result.ok) onArchived();
    else setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <SheetHeader>
          <SheetTitle>{t("title", { name: plan.deceasedName })}</SheetTitle>
          <SheetDescription>{t("description")}</SheetDescription>
        </SheetHeader>
        <SheetBody>
          <FormError>{failure && errorMessage(failure)}</FormError>
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose}>
            {t("keep")}
          </Button>
          <Button variant="danger" loading={busy} onClick={() => void archive()}>
            {t("confirm")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
