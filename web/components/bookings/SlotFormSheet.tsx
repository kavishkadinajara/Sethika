"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { createSlot, listSlots, updateSlot, type BookingFailure } from "@/lib/bookings/api";
import { toggleDay, weekdayDate } from "@/lib/bookings/calendar";
import { emptySlotForm, formAfterStale, malformedSlotFields, nameError, slotFormFrom, slotInputFrom, type SlotForm } from "@/lib/bookings/slot-form";
import type { BookingMode, FulfilmentMode, InstitutionMealSlot } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";
import { Switch } from "./Switch";
import { useBookingError } from "./useBookingError";
import { useWide } from "./useWide";
import { useDates } from "./useDates";

const OFFERABLE: FulfilmentMode[] = ["DonorBrings", "InstitutionPrepares"];

/** Fields the API can name; each has its own message under Slots.form.errors. */
type FieldName =
  | "name" | "nameSi" | "nameTa" | "startTime" | "endTime" | "daysOfWeekMask" | "bookingMode" | "maxSponsors"
  | "minHeadcount" | "maxHeadcount" | "estimatedCostPerHead" | "minAdvanceDays" | "maxAdvanceDays" | "allowedFulfilment";

/**
 * A meal slot, new or edited. An edit sends back the version it started from, so two people
 * editing one slot cannot overwrite each other (§8, E17): the second is told to reload.
 */
export function SlotFormSheet({
  slot,
  onClose,
  onSaved,
}: {
  /** The slot to edit, or null for a new one. */
  slot: InstitutionMealSlot | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations("Slots.form");
  const booking = useTranslations("Booking");
  const dates = useDates();
  const wide = useWide();
  const errorMessage = useBookingError();
  const [form, setForm] = useState<SlotForm>(() => (slot ? slotFormFrom(slot) : emptySlotForm()));
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  function set<K extends keyof SlotForm>(key: K, value: SlotForm[K]) {
    setForm((current) => ({ ...current, [key]: value }));
    // A new name may be free: the "already have a meal with this name" goes with the old one.
    if (key === "name" && failure?.code === "slot_name_taken") setFailure(null);
    setInvalid((current) => {
      if (!current.has(key)) return current;
      const next = new Set(current);
      next.delete(key);
      return next;
    });
  }

  const error = (field: FieldName) => (invalid.has(field) ? t(`errors.${field}`) : null);
  const nameState = nameError(invalid, failure?.code);
  const nameMessage = nameState === "taken" && failure ? errorMessage(failure) : nameState === "invalid" ? t("errors.name") : null;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);

    const malformed = malformedSlotFields(form);
    if (malformed.length > 0) {
      setInvalid(new Set(malformed));
      return;
    }

    setSaving(true);
    const input = slotInputFrom(form);
    const result = slot ? await updateSlot(slot.publicId, input) : await createSlot(input);
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) {
      setInvalid(new Set(result.failure.fields));
      return;
    }
    if (result.failure.code === "slot_name_taken") {
      setInvalid(new Set(["name"]));
    }
    // Someone saved the meal meanwhile: show it as it is now, with its new version, as the message
    // says, so the next save is checked against what this person has seen (QA DEF-006).
    if (result.failure.code === "stale" && slot) {
      const current = await listSlots();
      if (current.ok) {
        const fresh = formAfterStale(current.value, slot.publicId);
        if (!fresh) {
          onSaved();
          return;
        }
        setForm(fresh);
        setInvalid(new Set());
      }
    }
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{slot ? t("editTitle") : t("newTitle")}</SheetTitle>
            <SheetDescription>{slot ? t("editDescription") : t("newDescription")}</SheetDescription>
          </SheetHeader>

          <SheetBody className="grid gap-5">
            <Field label={t("name")} description={t("nameHint")} error={nameMessage} required>
              <Input value={form.name} onChange={(e) => set("name", e.target.value)} maxLength={100} autoComplete="off" />
            </Field>
            <div className="grid gap-5 sm:grid-cols-2">
              <Field label={t("nameSi")} error={error("nameSi")}>
                <Input lang="si" value={form.nameSi} onChange={(e) => set("nameSi", e.target.value)} maxLength={100} autoComplete="off" />
              </Field>
              <Field label={t("nameTa")} error={error("nameTa")}>
                <Input lang="ta" value={form.nameTa} onChange={(e) => set("nameTa", e.target.value)} maxLength={100} autoComplete="off" />
              </Field>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <Field label={t("startTime")} error={error("startTime")} required>
                <Input type="time" value={form.startTime} onChange={(e) => set("startTime", e.target.value)} />
              </Field>
              <Field label={t("endTime")} error={error("endTime")} required>
                <Input type="time" value={form.endTime} onChange={(e) => set("endTime", e.target.value)} />
              </Field>
            </div>

            <fieldset className="grid gap-2">
              <legend className="mb-1 text-base font-medium text-fg">{t("days")}</legend>
              <div className="flex flex-wrap gap-2">
                {[0, 1, 2, 3, 4, 5, 6].map((day) => {
                  const on = (form.daysOfWeekMask & (1 << day)) !== 0;
                  return (
                    <button
                      key={day}
                      type="button"
                      aria-pressed={on}
                      onClick={() => set("daysOfWeekMask", toggleDay(form.daysOfWeekMask, day))}
                      className={cn(
                        "min-h-11 min-w-11 rounded-full border-[1.5px] px-3 text-sm font-medium transition-colors duration-150",
                        focusRing,
                        on ? "border-primary bg-primary text-on-primary" : "border-line bg-surface text-fg hover:border-brand",
                      )}
                    >
                      {dates.weekday(weekdayDate(day), "short")}
                    </button>
                  );
                })}
              </div>
              {invalid.has("daysOfWeekMask") && <p className="text-sm text-danger">{t("errors.daysOfWeekMask")}</p>}
            </fieldset>

            <fieldset className="grid gap-2">
              <legend className="mb-1 text-base font-medium text-fg">{t("mode")}</legend>
              <div role="radiogroup" className="grid gap-2 sm:grid-cols-2">
                {(["Exclusive", "Shared"] as BookingMode[]).map((mode) => (
                  <label
                    key={mode}
                    className={cn(
                      "flex min-h-14 cursor-pointer items-start gap-3 rounded-cup border-[1.5px] bg-surface px-4 py-3",
                      "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                      form.bookingMode === mode ? "border-brand bg-brand/6" : "border-hairline",
                    )}
                  >
                    <input
                      type="radio"
                      name="bookingMode"
                      checked={form.bookingMode === mode}
                      onChange={() => set("bookingMode", mode)}
                      className="mt-1 size-5 accent-[var(--color-brand)]"
                    />
                    <span className="grid gap-0.5">
                      <span className="font-medium text-fg">{t(`modes.${mode}`)}</span>
                      <span className="text-sm text-muted">{t(`modeHints.${mode}`)}</span>
                    </span>
                  </label>
                ))}
              </div>
            </fieldset>

            {form.bookingMode === "Shared" && (
              <Field label={t("maxSponsors")} description={t("maxSponsorsHint")} error={error("maxSponsors")} required>
                <Input inputMode="numeric" value={form.maxSponsors} onChange={(e) => set("maxSponsors", e.target.value)} className="w-28" />
              </Field>
            )}

            <div className="grid grid-cols-2 gap-4">
              <Field label={t("minHeadcount")} error={error("minHeadcount")}>
                <Input inputMode="numeric" value={form.minHeadcount} onChange={(e) => set("minHeadcount", e.target.value)} />
              </Field>
              <Field label={t("maxHeadcount")} error={error("maxHeadcount")} required>
                <Input inputMode="numeric" value={form.maxHeadcount} onChange={(e) => set("maxHeadcount", e.target.value)} />
              </Field>
            </div>

            <Field label={t("cost")} description={t("costHint")} error={error("estimatedCostPerHead")}>
              <Input inputMode="decimal" value={form.estimatedCostPerHead} onChange={(e) => set("estimatedCostPerHead", e.target.value)} className="w-40" />
            </Field>

            <div className="grid grid-cols-2 gap-4">
              <Field label={t("minAdvance")} description={t("minAdvanceHint")} error={error("minAdvanceDays")}>
                <Input inputMode="numeric" value={form.minAdvanceDays} onChange={(e) => set("minAdvanceDays", e.target.value)} />
              </Field>
              <Field label={t("maxAdvance")} description={t("maxAdvanceHint")} error={error("maxAdvanceDays")}>
                <Input inputMode="numeric" value={form.maxAdvanceDays} onChange={(e) => set("maxAdvanceDays", e.target.value)} />
              </Field>
            </div>

            <fieldset className="grid gap-2">
              <legend className="mb-1 text-base font-medium text-fg">{t("fulfilment")}</legend>
              {OFFERABLE.map((mode) => (
                <label key={mode} className="flex min-h-11 cursor-pointer items-center gap-3 text-fg">
                  <input
                    type="checkbox"
                    checked={form.allowedFulfilment.includes(mode)}
                    onChange={(e) =>
                      set(
                        "allowedFulfilment",
                        e.target.checked ? OFFERABLE.filter((m) => m === mode || form.allowedFulfilment.includes(m)) : form.allowedFulfilment.filter((m) => m !== mode),
                      )
                    }
                    className="size-5 accent-[var(--color-brand)]"
                  />
                  {booking(`fulfilment.${mode}`)}
                </label>
              ))}
              {invalid.has("allowedFulfilment") && <p className="text-sm text-danger">{t("errors.allowedFulfilment")}</p>}
            </fieldset>

            <Switch checked={form.requiresApproval} onChange={(value) => set("requiresApproval", value)}>
              <span className="grid gap-0.5">
                <span className="font-medium">{t("requiresApproval")}</span>
                <span className="text-sm text-muted">{form.requiresApproval ? t("requiresApprovalOn") : t("requiresApprovalOff")}</span>
              </span>
            </Switch>

            {slot && <p className="rounded-cup bg-subtle px-4 py-3 text-sm text-fg">{t("snapshotNote")}</p>}

            <FormError>{failure && failure.code !== "slot_name_taken" && errorMessage(failure)}</FormError>
            {invalid.size > 0 && <p className="text-sm text-danger" aria-live="polite">{t("fixFields")}</p>}
          </SheetBody>

          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {slot ? t("save") : t("create")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
