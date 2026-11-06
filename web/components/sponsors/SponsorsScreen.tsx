"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
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
import { listSlots, type BookingFailure } from "@/lib/bookings/api";
import type { InstitutionMealSlot } from "@/lib/bookings/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { colomboParts } from "@/lib/services/time";
import { createDonor, listDonors, listSponsorships, updateDonor } from "@/lib/sponsors/api";
import { localPhone, searchDonors } from "@/lib/sponsors/donors";
import { byDonor } from "@/lib/sponsors/pattern";
import type { Donor, DonorLanguage, Sponsorship } from "@/lib/sponsors/types";
import { cn, focusRing } from "@/lib/utils";
import { NewSponsorshipSheet } from "./NewSponsorshipSheet";
import { usePatternSummary } from "./PatternSummary";
import { SponsorshipSheet } from "./SponsorshipSheet";
import { useSponsorError } from "./useSponsorError";

const LANGUAGES: DonorLanguage[] = ["si", "ta", "en"];
const MAX_NOTES = 500;

/**
 * The institution's donor book (plan 08, K1): the people who give regularly, many of whom never use
 * the app. Staff add them, change their details and record whether they agreed to receive texts
 * (Q2). A person whose phone is a Sethika account's is linked by the server (K14). Each card lists the
 * person's regular arrangements (K2); one opens with its next days, to skip a day, end it or swap a
 * day (K5, K8), and a new one is set up from the card with a preview of its days (K9).
 */
export function SponsorsScreen() {
  const t = useTranslations("Sponsors.book");
  const dates = useDates();
  const errorMessage = useSponsorError();
  const { can } = usePermissions();
  const canCreate = can("BOOKINGS.SPONSORS.CREATE");
  const canEdit = can("BOOKINGS.SPONSORS.EDIT");

  const [donors, setDonors] = useState<Donor[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [editing, setEditing] = useState<Donor | "new" | null>(null);
  const [query, setQuery] = useState("");
  const [sponsorships, setSponsorships] = useState<Sponsorship[]>([]);
  const [slots, setSlots] = useState<InstitutionMealSlot[]>([]);
  const [settingUp, setSettingUp] = useState<Donor | null>(null);
  const [opened, setOpened] = useState<string | null>(null);

  // The book decides the page; the arrangements and meals are beside it, left out if they cannot be read.
  const load = useCallback(async () => {
    setFailure(null);
    const [result, sponsorshipsResult, slotsResult] = await Promise.all([listDonors(), listSponsorships(), listSlots()]);
    if (sponsorshipsResult.ok) setSponsorships(sponsorshipsResult.value);
    if (slotsResult.ok) setSlots(slotsResult.value);
    if (!result.ok) return setFailure(result.failure);
    setDonors(result.value);
  }, []);

  useEffect(() => {
    // Loading the screen's data is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const found = useMemo(() => (donors ? searchDonors(donors, query) : []), [donors, query]);
  const arrangements = useMemo(() => byDonor(sponsorships), [sponsorships]);
  const summary = usePatternSummary();

  if (failure && !donors) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!donors) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <div className="grid gap-6">
      <FormError>{failure && errorMessage(failure)}</FormError>

      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 className="text-xl font-semibold text-brand">{t("heading", { count: donors.length })}</h2>
        {canCreate && <Button onClick={() => setEditing("new")}>{t("add")}</Button>}
      </div>

      {donors.length === 0 ? (
        <EmptyState>{t("none")}</EmptyState>
      ) : (
        <>
          <Field label={t("search")} description={t("searchHint")}>
            <Input type="search" value={query} onChange={(e) => setQuery(e.target.value)} autoComplete="off" className="max-w-md" />
          </Field>

          {found.length === 0 ? (
            <p className="text-muted">{t("noMatch", { query: query.trim() })}</p>
          ) : (
            <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
              {found.map((donor) => (
                <li key={donor.publicId}>
                  <article className={cn("grid h-full content-start gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift", !donor.isActive && "opacity-70")}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <h3 className="min-w-0 break-words text-lg font-semibold leading-snug text-brand">{donor.fullName}</h3>
                      {donor.hasAccount && <span className="rounded-full border border-accent/60 bg-accent/10 px-3 py-1 text-sm text-fg">{t("usesApp")}</span>}
                    </div>
                    <p className="text-sm text-muted">
                      <span className="tabular-nums">{localPhone(donor.phone)}</span> · {t(`language.${donor.language}`)}
                    </p>
                    <p className="text-sm text-fg">
                      {donor.textConsentAt
                        ? t("textsYes", { date: dates.medium(colomboParts(donor.textConsentAt).date) })
                        : donor.hasAccount
                          ? t("textsApp")
                          : t("textsNo")}
                    </p>
                    <p className="text-sm text-muted">{t("sponsorships", { count: donor.activeSponsorships })}</p>
                    {donor.notes && <p className="whitespace-pre-line break-words text-sm text-fg">{donor.notes}</p>}
                    {(arrangements.get(donor.publicId)?.length ?? 0) > 0 && (
                      <ul className="grid gap-1.5 pt-1" aria-label={t("arrangementsOf", { name: donor.fullName })}>
                        {arrangements.get(donor.publicId)!.map((s) => (
                          <li key={s.publicId}>
                            <button
                              type="button"
                              onClick={() => setOpened(s.publicId)}
                              className={cn(
                                "grid w-full gap-0.5 rounded-cup border border-hairline bg-subtle/40 px-3 py-2 text-left text-sm hover:border-brand",
                                focusRing,
                                s.status === "Ended" && "opacity-70",
                              )}
                            >
                              <span className="font-medium text-brand">
                                {s.mealSlotName}
                                {s.status === "Ended" && <span className="font-normal text-muted"> · {t("ended")}</span>}
                              </span>
                              <span className="break-words text-fg">{summary(s)}</span>
                            </button>
                          </li>
                        ))}
                      </ul>
                    )}
                    {(canEdit || (canCreate && donor.isActive)) && (
                      <div className="flex flex-wrap gap-2 pt-1">
                        {canCreate && donor.isActive && (
                          <Button variant="outline" onClick={() => setSettingUp(donor)} aria-label={t("setUpNamed", { name: donor.fullName })}>
                            {t("setUp")}
                          </Button>
                        )}
                        {canEdit && (
                          <Button variant="ghost" onClick={() => setEditing(donor)} aria-label={t("editNamed", { name: donor.fullName })}>
                            {t("edit")}
                          </Button>
                        )}
                      </div>
                    )}
                  </article>
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      {settingUp && <NewSponsorshipSheet donor={settingUp} slots={slots} onClose={() => setSettingUp(null)} onSaved={() => void load()} />}

      {opened && <SponsorshipSheet publicId={opened} all={sponsorships} onClose={() => setOpened(null)} onChanged={() => void load()} />}

      {editing && (
        <DonorSheet
          donor={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void load();
          }}
        />
      )}
    </div>
  );
}

type DonorField = "fullName" | "phone" | "notes" | "language";

/** A person, new or edited. The server normalises the phone and links an account with it (K14). */
function DonorSheet({ donor, onClose, onSaved }: { donor: Donor | null; onClose: () => void; onSaved: () => void }) {
  const t = useTranslations("Sponsors.form");
  const languages = useTranslations("Sponsors.book.language");
  const wide = useWide();
  const errorMessage = useSponsorError();
  const [fullName, setFullName] = useState(donor?.fullName ?? "");
  const [phone, setPhone] = useState(donor ? localPhone(donor.phone) : "");
  const [notes, setNotes] = useState(donor?.notes ?? "");
  const [language, setLanguage] = useState<DonorLanguage>(donor?.language ?? "si");
  const [textConsent, setTextConsent] = useState(donor?.textConsentAt != null);
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  const error = (field: DonorField) => (invalid.has(field) ? t(`errors.${field}`) : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    const missing = new Set<string>();
    if (fullName.trim() === "") missing.add("fullName");
    if (phone.trim() === "") missing.add("phone");
    setInvalid(missing);
    if (missing.size > 0) return;

    setSaving(true);
    const input = {
      fullName: fullName.trim(),
      phone: phone.trim(),
      notes: notes.trim() || null,
      language,
      textConsent,
      version: donor?.version,
    };
    const result = donor ? await updateDonor(donor.publicId, input) : await createDonor(input);
    setSaving(false);

    if (result.ok) return onSaved();
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    if (result.failure.code === "donor_phone_taken") return setInvalid(new Set(["phoneTaken"]));
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{donor ? t("editTitle") : t("newTitle")}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <Field label={t("fullName")} error={error("fullName")} required>
              <Input value={fullName} onChange={(e) => setFullName(e.target.value)} maxLength={150} autoComplete="off" />
            </Field>
            <Field label={t("phone")} description={t("phoneHint")} error={invalid.has("phoneTaken") ? t("errors.phoneTaken") : error("phone")} required>
              <Input type="tel" inputMode="tel" value={phone} onChange={(e) => setPhone(e.target.value)} maxLength={20} autoComplete="off" />
            </Field>
            <Field label={t("language")} description={t("languageHint")} error={error("language")}>
              <select value={language} onChange={(e) => setLanguage(e.target.value as DonorLanguage)} className={cn(inputClasses, "w-48")}>
                {LANGUAGES.map((code) => (
                  <option key={code} value={code}>
                    {languages(code)}
                  </option>
                ))}
              </select>
            </Field>
            <Switch checked={textConsent} onChange={setTextConsent}>
              <span className="grid gap-0.5">
                <span className="font-medium">{t("textConsent")}</span>
                <span className="text-sm text-muted">{t("textConsentHint")}</span>
              </span>
            </Switch>
            <Field label={t("notes")} description={t("notesHint")} error={error("notes")}>
              <textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={MAX_NOTES} rows={3} className={cn(inputClasses, "resize-y")} />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {donor ? t("save") : t("create")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
