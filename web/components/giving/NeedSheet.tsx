"use client";

import { useLocale, useTranslations } from "next-intl";
import { useMemo, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import type { BookingFailure } from "@/lib/bookings/api";
import { localName } from "@/lib/bookings/names";
import { addNeed, changeNeed } from "@/lib/giving/api";
import type { NeedListItem, NeedLookups, NeedPriority } from "@/lib/giving/types";
import { colomboParts } from "@/lib/services/time";
import { cn } from "@/lib/utils";
import { useGivingError } from "./useGivingError";

const priorities: NeedPriority[] = ["Low", "Normal", "Urgent"];

/**
 * Adding a need, as a draft nobody sees yet, or changing one (plan 09, M11). The quantity never goes below
 * what is spoken for, and the unit stays once anything is: the server says so in words.
 */
export function NeedSheet({ need, lookups, onClose, onSaved }: { need: NeedListItem | null; lookups: NeedLookups; onClose: () => void; onSaved: () => void }) {
  const t = useTranslations("Giving.needForm");
  const priorityName = useTranslations("Giving.priority");
  const locale = useLocale();
  const wide = useWide();
  const errorMessage = useGivingError();
  const today = useMemo(() => colomboParts(new Date().toISOString()).date, []);

  const [title, setTitle] = useState(need?.title ?? "");
  const [description, setDescription] = useState(need?.description ?? "");
  const [category, setCategory] = useState(need?.category ?? lookups.categories[0]?.code ?? "");
  const [unit, setUnit] = useState(need?.unit ?? lookups.units[0]?.code ?? "");
  const [quantity, setQuantity] = useState(need ? String(need.quantityNeeded) : "");
  const [priority, setPriority] = useState<NeedPriority>(need?.priority ?? "Normal");
  const [deadline, setDeadline] = useState(need?.deadline ?? "");
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  const error = (field: "title" | "description" | "category" | "unit" | "quantity" | "priority" | "deadline") =>
    invalid.has(field) ? t(`errors.${field}`) : null;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);

    const value = Number(quantity.trim().replaceAll(",", ""));
    const missing = new Set<string>();
    if (title.trim() === "") missing.add("title");
    if (quantity.trim() === "" || !(value > 0)) missing.add("quantity");
    setInvalid(missing);
    if (missing.size > 0) return;

    const input = { title: title.trim(), description: description.trim() || null, category, unit, quantity: value, priority, deadline: deadline || null };
    setSaving(true);
    const result = need ? await changeNeed(need.publicId, { ...input, version: need.version }) : await addNeed(input);
    setSaving(false);

    if (result.ok) return onSaved();
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{need ? t("editTitle") : t("addTitle")}</SheetTitle>
            <SheetDescription>{need ? t("editDescription") : t("addDescription")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <Field label={t("title")} description={t("titleHint")} error={error("title")} required>
              <Input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={150} autoComplete="off" />
            </Field>
            <Field label={t("description")} error={error("description")}>
              <textarea value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} rows={3} className={cn(inputClasses, "resize-y")} />
            </Field>
            <Field label={t("category")} error={error("category")}>
              <select value={category} onChange={(e) => setCategory(e.target.value)} className={cn(inputClasses, "w-full")}>
                {lookups.categories.map((c) => (
                  <option key={c.code} value={c.code}>
                    {localName(locale, c)}
                  </option>
                ))}
              </select>
            </Field>
            <div className="grid gap-5 sm:grid-cols-2">
              <Field label={t("quantity")} error={error("quantity")} required>
                <Input inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} autoComplete="off" />
              </Field>
              <Field label={t("unit")} error={error("unit")}>
                <select value={unit} onChange={(e) => setUnit(e.target.value)} className={cn(inputClasses, "w-full")}>
                  {lookups.units.map((u) => (
                    <option key={u.code} value={u.code}>
                      {localName(locale, u)}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            <div className="grid gap-5 sm:grid-cols-2">
              <Field label={t("priority")} error={error("priority")}>
                <select value={priority} onChange={(e) => setPriority(e.target.value as NeedPriority)} className={cn(inputClasses, "w-full")}>
                  {priorities.map((p) => (
                    <option key={p} value={p}>
                      {priorityName(p)}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label={t("deadline")} error={error("deadline")}>
                <Input type="date" value={deadline} min={today} onChange={(e) => setDeadline(e.target.value)} />
              </Field>
            </div>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {need ? t("saveEdit") : t("saveAdd")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
