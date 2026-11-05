"use client";

import { useTranslations } from "next-intl";
import { useMemo, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { DonorSheetGate } from "@/components/bookings/DonorSheetGate";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import type { BookingFailure } from "@/lib/bookings/api";
import { pledge } from "@/lib/giving/api";
import { formatQuantity } from "@/lib/giving/money";
import { needLeft } from "@/lib/giving/needs";
import type { PublicNeed } from "@/lib/giving/types";
import { colomboParts } from "@/lib/services/time";
import { useGivingError } from "./useGivingError";

/**
 * Pledging part of a need (plan 09, M13): how many, and when it may come. Asks for a sign-in only here,
 * and comes back to the same need (`back`). The server has the last word on what is left: two donors can
 * reach for the last units at once, and the one who loses is told so here.
 */
export function PledgeSheet({ need, unit, back, onClose, onPledged }: { need: PublicNeed; unit: string; back: string; onClose: () => void; onPledged: () => void }) {
  const t = useTranslations("Giving.pledge");
  const wide = useWide();

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <SheetHeader>
          <SheetTitle>{t("title", { need: need.title })}</SheetTitle>
          <SheetDescription>{t("left", { count: formatQuantity(needLeft(need)), unit })}</SheetDescription>
        </SheetHeader>
        <DonorSheetGate
          back={back}
          permission="MYGIVING.DONATIONS.CREATE"
          signInFirst={t("signInFirst")}
          institutionSession={t("institutionSession")}
          notAllowed={t("notAllowed")}
        >
          <PledgeForm need={need} unit={unit} onClose={onClose} onPledged={onPledged} />
        </DonorSheetGate>
      </SheetContent>
    </Sheet>
  );
}

function PledgeForm({ need, unit, onClose, onPledged }: { need: PublicNeed; unit: string; onClose: () => void; onPledged: () => void }) {
  const t = useTranslations("Giving.pledge");
  const errorMessage = useGivingError();
  const today = useMemo(() => colomboParts(new Date().toISOString()).date, []);
  const [quantity, setQuantity] = useState("");
  const [expected, setExpected] = useState("");
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);

    const value = Number(quantity.trim().replaceAll(",", ""));
    if (quantity.trim() === "" || !(value > 0)) return setInvalid(new Set(["quantity"]));
    setInvalid(new Set());

    setSaving(true);
    const result = await pledge(need.publicId, value, expected || null);
    setSaving(false);

    if (result.ok) return onPledged();
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  return (
    <form onSubmit={submit} noValidate className="contents">
      <SheetBody className="grid gap-5">
        <Field label={t("quantity", { unit })} error={invalid.has("quantity") ? t("quantityInvalid") : null} required>
          <Input inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} autoComplete="off" className="max-w-48" />
        </Field>
        <Field label={t("expected")} description={t("expectedHint")} error={invalid.has("expectedDate") ? t("expectedInvalid") : null}>
          <Input type="date" value={expected} min={today} onChange={(e) => setExpected(e.target.value)} className="max-w-48" />
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
  );
}
