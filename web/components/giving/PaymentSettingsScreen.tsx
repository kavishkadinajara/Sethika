"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import type { BookingFailure } from "@/lib/bookings/api";
import { getMerchant, saveMerchant } from "@/lib/giving/api";
import type { MerchantSettings } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { colomboParts } from "@/lib/services/time";
import { cn, focusRing } from "@/lib/utils";
import { useGivingError } from "./useGivingError";

/**
 * An institution's own PayHere merchant (plan 09, M10). The secret is write-only: the API never sends it
 * back, so the screen shows only when it was saved and by whom, and an empty box keeps it. Saving asks
 * for a step-up code (StepUpHost).
 */
export function PaymentSettingsScreen() {
  const t = useTranslations("Giving.payments");
  const errorMessage = useGivingError();
  const [merchant, setMerchant] = useState<MerchantSettings | null | undefined>(undefined);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await getMerchant();
    if (!result.ok) return setFailure(result.failure);
    setMerchant(result.value.merchant);
  }, []);

  useEffect(() => {
    // Loading the settings is what this component is for.
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

  if (merchant === undefined) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  // Keyed by version: a save reads the settings again, and the form starts from what was stored.
  return <MerchantForm key={merchant?.version ?? "new"} merchant={merchant} onSaved={() => void load()} />;
}

function MerchantForm({ merchant, onSaved }: { merchant: MerchantSettings | null; onSaved: () => void }) {
  const t = useTranslations("Giving.payments");
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useGivingError();
  const { can } = usePermissions();
  const canEdit = can("GIVING.PAYMENTS.EDIT");
  const [merchantId, setMerchantId] = useState(merchant?.merchantId ?? "");
  const [secret, setSecret] = useState("");
  const [enabled, setEnabled] = useState(merchant?.isEnabled ?? false);
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    setInvalid(new Set());

    setSaving(true);
    const result = await saveMerchant({ merchantId: merchantId.trim(), secret: secret.trim() || null, enabled, version: merchant?.version ?? null });
    setSaving(false);

    if (result.ok) {
      toast({ variant: "success", title: t("saved") });
      return onSaved();
    }

    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(result.failure);
  }

  return (
    <div className="grid max-w-2xl gap-6">
      <Card className="grid gap-2 p-4 text-fg sm:p-6">
        <p>{t("howTo")}</p>
        <p className="text-sm text-muted">{t("refunds")}</p>
      </Card>

      <form onSubmit={submit} noValidate className="grid gap-5">
        <Field label={t("merchantId")} error={invalid.has("merchantId") ? t("merchantIdInvalid") : null} required>
          <Input value={merchantId} onChange={(e) => setMerchantId(e.target.value)} autoComplete="off" maxLength={30} disabled={!canEdit} className="max-w-64" />
        </Field>

        <Field
          label={t("secret")}
          description={merchant ? t("secretKeep") : t("secretNew")}
          error={invalid.has("secret") ? t("secretInvalid") : null}
          required={!merchant}
        >
          <Input type="password" value={secret} onChange={(e) => setSecret(e.target.value)} autoComplete="new-password" maxLength={200} disabled={!canEdit} />
        </Field>

        <label className="flex min-h-11 cursor-pointer items-start gap-3 text-fg">
          <input
            type="checkbox"
            checked={enabled}
            disabled={!canEdit}
            onChange={(e) => setEnabled(e.target.checked)}
            className="mt-0.5 size-5 shrink-0 accent-[var(--color-brand)]"
          />
          <span className="grid gap-0.5">
            <span>{t("enabled")}</span>
            <span className="text-sm text-muted">{t("enabledHint")}</span>
          </span>
        </label>

        {merchant && (
          <p data-testid="merchant-saved" className="text-sm text-muted">
            {t("savedBy", { date: dates.medium(colomboParts(merchant.updatedAt).date), name: merchant.updatedByName })}
          </p>
        )}

        <FormError>{failure && (failure.code === "stale" ? t("stale") : errorMessage(failure))}</FormError>

        {canEdit ? (
          <Button type="submit" loading={saving} className="justify-self-start">
            {t("save")}
          </Button>
        ) : (
          <p className="text-fg">{t("readOnly")}</p>
        )}
      </form>
    </div>
  );
}
