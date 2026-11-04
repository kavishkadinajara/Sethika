"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import type { BookingFailure } from "@/lib/bookings/api";
import { voidDonation } from "@/lib/giving/api";
import type { DonationListItem } from "@/lib/giving/types";
import { cn } from "@/lib/utils";
import { useGiftText } from "./GiftText";
import { useGivingError } from "./useGivingError";

/**
 * Voiding a gift (plan 09, M6): never an edit, never a delete. A second person, with a reason; the
 * session asks for a step-up code first (StepUpHost). The gift stays in the list, marked, and leaves
 * every total; a mistake is put right by recording the gift again.
 */
export function VoidDonationSheet({ donation, onClose, onVoided }: { donation: DonationListItem; onClose: () => void; onVoided: () => void }) {
  const t = useTranslations("Giving.void");
  const wide = useWide();
  const errorMessage = useGivingError();
  const { gift } = useGiftText();
  const [reason, setReason] = useState("");
  const [invalid, setInvalid] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    if (reason.trim() === "") return setInvalid(true);
    setInvalid(false);

    setSaving(true);
    const result = await voidDonation(donation.receiptNo, donation.version, reason.trim());
    setSaving(false);

    if (result.ok) return onVoided();
    if (result.failure.code === "invalid_details") return setInvalid(true);
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("title", { receiptNo: donation.receiptNo })}</SheetTitle>
            <SheetDescription>{t("description", { gift: gift(donation), name: donation.donorName })}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <p className="text-fg">{t("explain")}</p>
            <Field label={t("reason")} description={t("reasonHint")} error={invalid ? t("reasonMissing") : null} required>
              <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} rows={3} className={cn(inputClasses, "resize-y")} />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" variant="danger" loading={saving}>
              {t("confirm")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
