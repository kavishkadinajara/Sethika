"use client";

import { useLocale, useTranslations } from "next-intl";
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { FormError } from "@/components/auth/FormError";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import type { BookingFailure } from "@/lib/bookings/api";
import { localName } from "@/lib/bookings/names";
import { recordDonation } from "@/lib/giving/api";
import type { CounterMethod, DonationKind, LookupItem } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { colomboParts } from "@/lib/services/time";
import { listDonors } from "@/lib/sponsors/api";
import { localPhone, searchDonors } from "@/lib/sponsors/donors";
import type { Donor } from "@/lib/sponsors/types";
import { cn } from "@/lib/utils";
import { useGivingError } from "./useGivingError";

type Field = "kind" | "amount" | "method" | "items" | "quantity" | "estimatedValue" | "date" | "donor" | "donorName" | "donorPhone" | "note";

/** A day `years` back from `day` (YYYY-MM-DD), for the date field's lower bound. */
function yearsBack(day: string, years: number): string {
  return `${Number(day.slice(0, 4)) - years}${day.slice(4)}`;
}

/** A typed figure as a number, or null when blank. */
function figure(text: string): number | null {
  const trimmed = text.trim().replaceAll(",", "");
  return trimmed === "" ? null : Number(trimmed);
}

/**
 * Recording a gift at the counter (plan 09, M1, M3): money with how it was paid, or items with how
 * many; the day it was given; and who gave it, from the donor book (their name and phone come from
 * the book, and their texts follow its consent, M5) or a walk-in's name typed once. The server gives
 * the receipt its number.
 */
export function RecordDonationSheet({ methods, onClose, onRecorded }: { methods: LookupItem[]; onClose: () => void; onRecorded: (receiptNo: string) => void }) {
  const t = useTranslations("Giving.record");
  const locale = useLocale();
  const wide = useWide();
  const errorMessage = useGivingError();
  const { can } = usePermissions();
  const readsBook = can("BOOKINGS.SPONSORS.VIEW");

  const today = useMemo(() => colomboParts(new Date().toISOString()).date, []);
  const [kind, setKind] = useState<DonationKind>("Money");
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<CounterMethod>("CASH");
  const [items, setItems] = useState("");
  const [quantity, setQuantity] = useState("");
  const [estimate, setEstimate] = useState("");
  const [date, setDate] = useState(today);
  const [fromBook, setFromBook] = useState(readsBook);
  const [donors, setDonors] = useState<Donor[]>([]);
  const [query, setQuery] = useState("");
  const [donor, setDonor] = useState<string | null>(null);
  const [donorName, setDonorName] = useState("");
  const [donorPhone, setDonorPhone] = useState("");
  const [note, setNote] = useState("");
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!readsBook) return;
    void listDonors().then((result) => result.ok && setDonors(result.value.filter((d) => d.isActive)));
  }, [readsBook]);

  const found = useMemo(() => searchDonors(donors, query).slice(0, 8), [donors, query]);
  const chosen = donors.find((d) => d.publicId === donor) ?? null;
  const error = (field: Field) => (invalid.has(field) ? t(`errors.${field}`) : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);

    const missing = new Set<string>();
    const money = kind === "Money";
    const amountValue = figure(amount);
    const quantityValue = figure(quantity);
    const estimateValue = figure(estimate);
    if (money && (amountValue === null || !(amountValue > 0))) missing.add("amount");
    if (!money && items.trim() === "") missing.add("items");
    if (!money && quantityValue !== null && !(quantityValue > 0)) missing.add("quantity");
    if (!money && estimateValue !== null && !(estimateValue > 0)) missing.add("estimatedValue");
    if (date === "") missing.add("date");
    if (fromBook && donor === null) missing.add("donor");
    if (!fromBook && donorName.trim() === "") missing.add("donorName");
    setInvalid(missing);
    if (missing.size > 0) return;

    setSaving(true);
    const result = await recordDonation({
      kind,
      amount: money ? amountValue : null,
      method: money ? method : null,
      items: money ? null : items.trim(),
      quantity: money ? null : quantityValue,
      estimatedValue: money ? null : estimateValue,
      date,
      donor: fromBook ? donor : null,
      donorName: fromBook ? null : donorName.trim(),
      donorPhone: fromBook || donorPhone.trim() === "" ? null : donorPhone.trim(),
      note: note.trim() || null,
    });
    setSaving(false);

    if (result.ok) return onRecorded(result.value.receiptNo);
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("title")}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <Choice legend={t("kind")}>
              <Radio name="kind" checked={kind === "Money"} onChange={() => setKind("Money")}>
                {t("kindMoney")}
              </Radio>
              <Radio name="kind" checked={kind === "InKind"} onChange={() => setKind("InKind")}>
                {t("kindItems")}
              </Radio>
            </Choice>

            {kind === "Money" ? (
              <>
                <Field label={t("amount")} description={t("amountHint")} error={error("amount")} required>
                  <Input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} autoComplete="off" className="max-w-48" />
                </Field>
                <Field label={t("method")} error={error("method")}>
                  <select value={method} onChange={(e) => setMethod(e.target.value as CounterMethod)} className={cn(inputClasses, "w-56")}>
                    {methods.map((m) => (
                      <option key={m.code} value={m.code}>
                        {localName(locale, m)}
                      </option>
                    ))}
                  </select>
                </Field>
              </>
            ) : (
              <>
                <Field label={t("items")} description={t("itemsHint")} error={error("items")} required>
                  <textarea value={items} onChange={(e) => setItems(e.target.value)} maxLength={500} rows={2} className={cn(inputClasses, "resize-y")} />
                </Field>
                <div className="grid gap-5 sm:grid-cols-2">
                  <Field label={t("quantity")} error={error("quantity")}>
                    <Input inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} autoComplete="off" />
                  </Field>
                  <Field label={t("estimatedValue")} description={t("estimatedValueHint")} error={error("estimatedValue")}>
                    <Input inputMode="decimal" value={estimate} onChange={(e) => setEstimate(e.target.value)} autoComplete="off" />
                  </Field>
                </div>
              </>
            )}

            <Field label={t("date")} description={t("dateHint")} error={error("date")} required>
              <Input type="date" value={date} min={yearsBack(today, 1)} max={today} onChange={(e) => setDate(e.target.value)} className="max-w-48" />
            </Field>

            <Choice legend={t("who")}>
              {readsBook && (
                <Radio name="who" checked={fromBook} onChange={() => setFromBook(true)}>
                  {t("fromBook")}
                </Radio>
              )}
              <Radio name="who" checked={!fromBook} onChange={() => setFromBook(false)}>
                {t("walkIn")}
              </Radio>
            </Choice>

            {fromBook ? (
              <div className="grid gap-2">
                <Field label={t("findDonor")} description={chosen ? t("chosen", { name: chosen.fullName }) : t("findDonorHint")} error={error("donor")} required>
                  <Input type="search" value={query} onChange={(e) => setQuery(e.target.value)} autoComplete="off" />
                </Field>
                {found.length > 0 && (
                  <ul className="grid gap-1" aria-label={t("donorChoices")}>
                    {found.map((d) => (
                      <li key={d.publicId}>
                        <Radio name="donor" checked={donor === d.publicId} onChange={() => setDonor(d.publicId)}>
                          <span className="font-medium">{d.fullName}</span> <span className="text-muted tabular-nums">· {localPhone(d.phone)}</span>
                        </Radio>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            ) : (
              <>
                <Field label={t("donorName")} error={error("donorName")} required>
                  <Input value={donorName} onChange={(e) => setDonorName(e.target.value)} maxLength={150} autoComplete="off" />
                </Field>
                <Field label={t("donorPhone")} description={t("donorPhoneHint")} error={error("donorPhone")}>
                  <Input type="tel" inputMode="tel" value={donorPhone} onChange={(e) => setDonorPhone(e.target.value)} maxLength={20} autoComplete="off" />
                </Field>
              </>
            )}

            <Field label={t("note")} description={t("noteHint")} error={error("note")}>
              <textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} rows={2} className={cn(inputClasses, "resize-y")} />
            </Field>

            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {t("save")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

function Choice({ legend, children }: { legend: string; children: ReactNode }) {
  return (
    <fieldset className="grid gap-2">
      <legend className="mb-2 font-medium text-fg">{legend}</legend>
      <div className="flex flex-wrap gap-x-6 gap-y-2">{children}</div>
    </fieldset>
  );
}

function Radio({ name, checked, onChange, children }: { name: string; checked: boolean; onChange: () => void; children: ReactNode }) {
  return (
    <label className="flex min-h-11 cursor-pointer items-center gap-3">
      <input type="radio" name={name} checked={checked} onChange={onChange} className="size-5 accent-[var(--color-brand)]" />
      <span>{children}</span>
    </label>
  );
}
