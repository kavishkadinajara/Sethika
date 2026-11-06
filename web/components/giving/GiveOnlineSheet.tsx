"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { DonorSheetGate } from "@/components/bookings/DonorSheetGate";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import type { BookingFailure } from "@/lib/bookings/api";
import { startGift } from "@/lib/giving/api";
import { parseGift } from "@/lib/giving/payments";
import type { CheckoutForm } from "@/lib/giving/types";
import { useGivingError } from "./useGivingError";

type GiveOnlineSheetProps = {
  slug: string;
  /** The institution's name, in the page's language. */
  place: string;
  /** The need the gift is for, when it came from a need's card. */
  need?: { publicId: string; title: string } | null;
  /** Where signing in comes back to, with the sheet open again. */
  back: string;
  onClose: () => void;
};

/**
 * Giving money online (plan 09, M8): how much, and the details PayHere requires. The API writes the order
 * and answers PayHere's signed form, which the browser posts, so the donor pays on PayHere's own page.
 * The merchant's secret never comes near the browser.
 */
export function GiveOnlineSheet({ slug, place, need = null, back, onClose }: GiveOnlineSheetProps) {
  const t = useTranslations("Giving.give");
  const wide = useWide();

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <SheetHeader>
          <SheetTitle>{need ? t("titleNeed", { need: need.title }) : t("title", { name: place })}</SheetTitle>
          <SheetDescription>{t("description", { name: place })}</SheetDescription>
        </SheetHeader>
        <DonorSheetGate
          back={back}
          permission="MYGIVING.DONATIONS.CREATE"
          signInFirst={t("signInFirst")}
          institutionSession={t("institutionSession")}
          notAllowed={t("notAllowed")}
        >
          <GiveForm slug={slug} need={need} onClose={onClose} />
        </DonorSheetGate>
      </SheetContent>
    </Sheet>
  );
}

/** Leaves for PayHere: a plain form post, because its checkout is a page and not an API. */
function postToPayHere(checkout: CheckoutForm) {
  const form = document.createElement("form");
  form.method = "POST";
  form.action = checkout.action;
  for (const [name, value] of Object.entries(checkout.fields)) {
    const input = document.createElement("input");
    input.type = "hidden";
    input.name = name;
    input.value = value;
    form.append(input);
  }
  document.body.append(form);
  form.submit();
}

function GiveForm({ slug, need, onClose }: { slug: string; need: { publicId: string; title: string } | null; onClose: () => void }) {
  const t = useTranslations("Giving.give");
  const errorMessage = useGivingError();
  const [amount, setAmount] = useState("");
  const [email, setEmail] = useState("");
  const [address, setAddress] = useState("");
  const [city, setCity] = useState("");
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);

    const value = parseGift(amount);
    const missing = new Set<string>();
    if (value === null) missing.add("amount");
    if (email.trim() === "") missing.add("email");
    if (address.trim() === "") missing.add("address");
    if (city.trim() === "") missing.add("city");
    setInvalid(missing);
    if (value === null || missing.size > 0) return;

    setSaving(true);
    const result = await startGift(slug, { amount: value, need: need?.publicId ?? null, email: email.trim(), address: address.trim(), city: city.trim() });

    // Still "saving" on success: the page is leaving for PayHere.
    if (result.ok) return postToPayHere(result.value);

    setSaving(false);
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  return (
    <form onSubmit={submit} noValidate className="contents">
      <SheetBody className="grid gap-5">
        <Field label={t("amount")} description={t("amountHint")} error={invalid.has("amount") ? t("amountInvalid") : null} required>
          <Input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} autoComplete="off" className="max-w-48" />
        </Field>
        <p className="text-sm text-muted">{t("detailsHint")}</p>
        <Field label={t("email")} error={invalid.has("email") ? t("emailInvalid") : null} required>
          <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" maxLength={256} />
        </Field>
        <Field label={t("address")} error={invalid.has("address") ? t("addressInvalid") : null} required>
          <Input value={address} onChange={(e) => setAddress(e.target.value)} autoComplete="street-address" maxLength={200} />
        </Field>
        <Field label={t("city")} error={invalid.has("city") ? t("cityInvalid") : null} required>
          <Input value={city} onChange={(e) => setCity(e.target.value)} autoComplete="address-level2" maxLength={100} />
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
