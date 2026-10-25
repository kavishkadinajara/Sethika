"use client";

import { useFormatter, useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import {
  addBlockedDate,
  getListing,
  listBlockedDates,
  listSlots,
  removeBlockedDate,
  setListed,
  setSlotActive,
  type BookingFailure,
} from "@/lib/bookings/api";
import { colomboToday, maskDays, weekdayDate } from "@/lib/bookings/calendar";
import { localName } from "@/lib/bookings/names";
import type { BlockedDate, InstitutionMealSlot, Listing } from "@/lib/bookings/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { EmptyState } from "./InstitutionDirectory";
import { SlotFormSheet } from "./SlotFormSheet";
import { Switch } from "./Switch";
import { useBookingError } from "./useBookingError";
import { useWide } from "./useWide";
import { useDates } from "./useDates";

/**
 * The institution's meals (plan 03, 5.4 and 5.7): its public page and listing, the slots donors
 * book, and the days it takes out. Changes that leave bookings in place - switching a slot off,
 * blocking days - say which bookings, since nothing here cancels them.
 */
export function SlotsScreen() {
  const t = useTranslations("Slots");
  const errorMessage = useBookingError();
  const { can } = usePermissions();
  const canEdit = can("BOOKINGS.SLOTS.EDIT");
  const canCreate = can("BOOKINGS.SLOTS.CREATE");

  const [slots, setSlots] = useState<InstitutionMealSlot[] | null>(null);
  const [blocks, setBlocks] = useState<BlockedDate[] | null>(null);
  const [listing, setListing] = useState<Listing | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [editing, setEditing] = useState<InstitutionMealSlot | "new" | null>(null);
  const [blocking, setBlocking] = useState(false);
  /** Bookings a change left in place, to say so until dismissed. */
  const [kept, setKept] = useState<string[]>([]);

  const load = useCallback(async () => {
    setFailure(null);
    const [s, b, l] = await Promise.all([listSlots(), listBlockedDates(), getListing()]);
    const failed = [s, b, l].find((r) => !r.ok);
    if (failed && !failed.ok) {
      setFailure(failed.failure);
      return;
    }
    if (s.ok) setSlots(s.value);
    if (b.ok) setBlocks(b.value);
    if (l.ok) setListing(l.value);
  }, []);

  useEffect(() => {
    // Loading the screen's data is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (failure && !slots) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!slots || !blocks || !listing) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const activeSlots = slots.filter((s) => s.isActive).length;

  return (
    <div className="grid gap-10">
      <ListingCard listing={listing} canEdit={canEdit} hasActiveSlot={activeSlots > 0} onChanged={(listed) => setListing({ ...listing, listed })} />

      {kept.length > 0 && <KeptNotice bookings={kept} onDismiss={() => setKept([])} />}

      <section className="grid gap-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-xl font-semibold text-brand">{t("mealsHeading")}</h2>
          {canCreate && <Button onClick={() => setEditing("new")}>{t("add")}</Button>}
        </div>

        {slots.length === 0 ? (
          <EmptyState>{t("noSlots")}</EmptyState>
        ) : (
          <ul className="grid gap-3">
            {slots.map((slot) => (
              <li key={slot.publicId}>
                <SlotCard
                  slot={slot}
                  canEdit={canEdit}
                  onEdit={() => setEditing(slot)}
                  onToggled={(bookings) => {
                    if (bookings.length > 0) setKept(bookings);
                    void load();
                  }}
                />
              </li>
            ))}
          </ul>
        )}
      </section>

      <BlockedDates
        blocks={blocks}
        canEdit={canEdit}
        onAdd={() => setBlocking(true)}
        onRemoved={() => void load()}
      />

      {editing && (
        <SlotFormSheet
          slot={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void load();
          }}
        />
      )}

      {blocking && (
        <BlockSheet
          slots={slots}
          onClose={() => setBlocking(false)}
          onAdded={(bookings) => {
            setBlocking(false);
            if (bookings.length > 0) setKept(bookings);
            void load();
          }}
        />
      )}
    </div>
  );
}

/** The one card that matters most here (the gold rim): where donors find this institution. */
function ListingCard({ listing, canEdit, hasActiveSlot, onChanged }: { listing: Listing; canEdit: boolean; hasActiveSlot: boolean; onChanged: (listed: boolean) => void }) {
  const t = useTranslations("Slots.listing");
  const toast = useToast();
  const errorMessage = useBookingError();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const link = typeof window === "undefined" ? `/i/${listing.slug}` : `${window.location.origin}/i/${listing.slug}`;

  async function change(listed: boolean) {
    setBusy(true);
    setFailure(null);
    const result = await setListed(listed);
    setBusy(false);
    if (result.ok) onChanged(listed);
    else setFailure(result.failure);
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(link);
      toast({ variant: "success", title: t("copied") });
    } catch {
      toast({ variant: "error", title: t("copyFailed") });
    }
  }

  return (
    <Card accent>
      <CardHeader>
        <CardTitle as="h2">{t("title")}</CardTitle>
        <CardDescription>{t("description")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        <div className="flex flex-wrap items-center gap-2">
          <code className="min-w-0 flex-1 break-all rounded-cup bg-subtle px-3 py-2.5 text-sm text-fg">{link}</code>
          <Button variant="outline" onClick={() => void copy()}>
            {t("copy")}
          </Button>
        </div>

        {canEdit ? (
          <Switch checked={listing.listed} onChange={(listed) => void change(listed)} busy={busy} disabled={!listing.listed && !hasActiveSlot}>
            <span className="grid gap-0.5">
              <span className="font-medium">{t("switch")}</span>
              <span className="text-sm text-muted">{!hasActiveSlot && !listing.listed ? t("needsSlot") : listing.listed ? t("listedHint") : t("unlistedHint")}</span>
            </span>
          </Switch>
        ) : (
          <p className="text-sm text-muted">{listing.listed ? t("listedHint") : t("unlistedHint")}</p>
        )}
        <FormError>{failure && errorMessage(failure)}</FormError>
      </CardContent>
    </Card>
  );
}

function SlotCard({ slot, canEdit, onEdit, onToggled }: { slot: InstitutionMealSlot; canEdit: boolean; onEdit: () => void; onToggled: (bookings: string[]) => void }) {
  const t = useTranslations("Slots");
  const booking = useTranslations("Booking");
  const locale = useLocale();
  const format = useFormatter();
  const dates = useDates();
  const errorMessage = useBookingError();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const days = maskDays(slot.daysOfWeekMask);

  async function toggle() {
    setBusy(true);
    setFailure(null);
    const result = await setSlotActive(slot.publicId, !slot.isActive);
    setBusy(false);
    if (result.ok) onToggled(result.value.bookings);
    else setFailure(result.failure);
  }

  return (
    <article className={cn("grid gap-3 rounded-bowl border border-hairline bg-surface p-5 shadow-lift", !slot.isActive && "opacity-75 shadow-none")}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="grid min-w-0 gap-0.5">
          <h3 className="text-lg font-semibold leading-snug text-brand">{localName(locale, slot)}</h3>
          <p className="tabular-nums text-fg">
            {slot.startTime}–{slot.endTime} ·{" "}
            {days.length === 7 ? t("everyDay") : days.map((d) => dates.weekday(weekdayDate(d), "short")).join(" ")}
          </p>
        </div>
        {!slot.isActive && <span className="rounded-full border border-hairline bg-subtle px-3 py-1 text-sm font-medium text-muted">{t("off")}</span>}
      </div>

      <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2">
        <Fact term={t("facts.mode")}>{slot.bookingMode === "Shared" ? t("sharedBy", { count: slot.maxSponsors }) : t("oneSponsor")}</Fact>
        <Fact term={t("facts.people")}>{t("peopleRange", { min: slot.minHeadcount, max: slot.maxHeadcount })}</Fact>
        <Fact term={t("facts.window")}>{t("window", { min: slot.minAdvanceDays, max: slot.maxAdvanceDays })}</Fact>
        <Fact term={t("facts.approval")}>{slot.requiresApproval ? t("approvalYes") : t("approvalNo")}</Fact>
        <Fact term={t("facts.fulfilment")}>{slot.allowedFulfilment.map((f) => booking(`fulfilment.${f}`)).join(", ")}</Fact>
        {slot.estimatedCostPerHead !== null && (
          <Fact term={t("facts.cost")}>{format.number(slot.estimatedCostPerHead, { style: "currency", currency: "LKR", minimumFractionDigits: 0, maximumFractionDigits: 2 })}</Fact>
        )}
      </dl>

      {canEdit && (
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={onEdit}>
            {t("edit")}
          </Button>
          <Button variant="ghost" loading={busy} onClick={() => void toggle()}>
            {slot.isActive ? t("turnOff") : t("turnOn")}
          </Button>
        </div>
      )}
      <FormError>{failure && errorMessage(failure)}</FormError>
    </article>
  );
}

function Fact({ term, children }: { term: string; children: ReactNode }) {
  return (
    <div className="flex gap-2">
      <dt className="text-muted">{term}</dt>
      <dd className="text-fg">{children}</dd>
    </div>
  );
}

function KeptNotice({ bookings, onDismiss }: { bookings: string[]; onDismiss: () => void }) {
  const t = useTranslations("Slots");
  return (
    <div role="status" className="grid gap-2 rounded-cup border border-accent/60 bg-accent/15 px-4 py-3 text-fg">
      <p>{t("kept", { count: bookings.length })}</p>
      <p className="text-sm tabular-nums">{bookings.join(", ")}</p>
      <div className="flex flex-wrap gap-2">
        <Button variant="ghost" onClick={onDismiss} className="-ml-3">
          {t("dismiss")}
        </Button>
      </div>
    </div>
  );
}

function BlockedDates({ blocks, canEdit, onAdd, onRemoved }: { blocks: BlockedDate[]; canEdit: boolean; onAdd: () => void; onRemoved: () => void }) {
  const t = useTranslations("Slots.blocks");
  const dates = useDates();
  const errorMessage = useBookingError();
  const [removing, setRemoving] = useState<number | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function remove(id: number) {
    setRemoving(id);
    setFailure(null);
    const result = await removeBlockedDate(id);
    setRemoving(null);
    if (result.ok) onRemoved();
    else setFailure(result.failure);
  }

  const range = (block: BlockedDate) => dates.range(block.dateFrom, block.dateTo);

  return (
    <section className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="grid gap-1">
          <h2 className="text-xl font-semibold text-brand">{t("heading")}</h2>
          <p className="text-sm text-muted">{t("description")}</p>
        </div>
        {canEdit && (
          <Button variant="outline" onClick={onAdd}>
            {t("add")}
          </Button>
        )}
      </div>

      {blocks.length === 0 ? (
        <p className="text-muted">{t("none")}</p>
      ) : (
        <ul className="grid gap-2">
          {blocks.map((block) => (
            <li key={block.id} className="flex flex-wrap items-center justify-between gap-3 rounded-cup border border-hairline bg-surface px-4 py-3">
              <div className="grid min-w-0 gap-0.5">
                <p className="font-medium text-fg">{range(block)}</p>
                <p className="text-sm text-muted">
                  {block.mealSlotName ?? t("allMeals")} · {block.reason}
                </p>
              </div>
              {canEdit && (
                <Button variant="ghost" loading={removing === block.id} onClick={() => void remove(block.id)} className="text-danger hover:bg-danger/10">
                  {t("remove")}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      <FormError>{failure && errorMessage(failure)}</FormError>
    </section>
  );
}

function BlockSheet({ slots, onClose, onAdded }: { slots: InstitutionMealSlot[]; onClose: () => void; onAdded: (bookings: string[]) => void }) {
  const t = useTranslations("Slots.blocks");
  const locale = useLocale();
  const wide = useWide();
  const errorMessage = useBookingError();
  const today = colomboToday();
  const [dateFrom, setDateFrom] = useState(today);
  const [dateTo, setDateTo] = useState(today);
  const [mealSlot, setMealSlot] = useState("");
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const fields = new Set(failure?.fields ?? []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setFailure(null);
    const result = await addBlockedDate({ dateFrom, dateTo, mealSlot: mealSlot || null, reason: reason.trim() });
    setSaving(false);
    if (result.ok) onAdded(result.value.bookings);
    else setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("sheetTitle")}</SheetTitle>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <div className="grid grid-cols-2 gap-4">
              <Field label={t("from")} error={fields.has("dateFrom") ? t("errors.dateFrom") : null} required>
                <Input type="date" min={today} value={dateFrom} onChange={(e) => {
                  setDateFrom(e.target.value);
                  if (dateTo < e.target.value) setDateTo(e.target.value);
                }} />
              </Field>
              <Field label={t("to")} error={fields.has("dateTo") ? t("errors.dateTo") : null} required>
                <Input type="date" min={dateFrom || today} value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
              </Field>
            </div>
            <Field label={t("which")}>
              <select value={mealSlot} onChange={(e) => setMealSlot(e.target.value)} className={inputClasses}>
                <option value="">{t("allMeals")}</option>
                {slots.map((slot) => (
                  <option key={slot.publicId} value={slot.publicId}>
                    {localName(locale, slot)}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t("reason")} description={t("reasonHint")} error={fields.has("reason") ? t("errors.reason") : null} required>
              <Input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={200} />
            </Field>
            <p className="text-sm text-muted">{t("keepsBookings")}</p>
            <FormError>{failure && failure.fields.length === 0 && errorMessage(failure)}</FormError>
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
