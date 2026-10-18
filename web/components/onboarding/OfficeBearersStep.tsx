"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { AuthHeading } from "@/components/auth/AuthHeading";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { replaceOfficeBearers } from "@/lib/onboarding/api";
import { bearerFieldErrors, emptyBearer, MAX_OFFICE_BEARERS } from "@/lib/onboarding/progress";
import type { OfficeBearer } from "@/lib/onboarding/types";
import { useOnboardingError } from "./useOnboardingError";

const fields = ["fullName", "nic", "designation", "phone", "email"] as const;

/** Step 2: the people who run the institution, saved as one list (the API replaces the set). */
export function OfficeBearersStep({
  requestNo,
  initial,
  focusOnMount,
  onSaved,
  onBack,
}: {
  requestNo: string;
  initial: OfficeBearer[];
  focusOnMount: boolean;
  onSaved: (bearers: OfficeBearer[]) => void;
  onBack: () => void;
}) {
  const t = useTranslations("Apply");
  const errorMessage = useOnboardingError();
  const [bearers, setBearers] = useState<OfficeBearer[]>(initial.length > 0 ? initial : [emptyBearer(true)]);
  const [errors, setErrors] = useState(bearerFieldErrors([]));
  const [formError, setFormError] = useState<ReactNode>(null);
  const [busy, setBusy] = useState(false);

  const update = (index: number, change: Partial<OfficeBearer>) =>
    setBearers((previous) => previous.map((b, i) => (i === index ? { ...b, ...change } : b)));

  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setFormError(null);
    setErrors(bearerFieldErrors([]));
    if (!bearers.some((b) => b.isSignatory)) {
      setFormError(t("bearers.needSignatory"));
      return;
    }

    setBusy(true);
    const result = await replaceOfficeBearers(requestNo, bearers);
    setBusy(false);
    if (result.ok) {
      onSaved(bearers);
      return;
    }
    if (result.failure.fields.length > 0) {
      setErrors(bearerFieldErrors(result.failure.fields));
      setFormError(t("fieldsInvalid"));
    } else {
      setFormError(errorMessage(result.failure));
    }
  }

  return (
    <form noValidate onSubmit={save} className="grid gap-5">
      <AuthHeading title={t("bearers.title")} description={t("bearers.help")} focusOnMount={focusOnMount} />
      <FormError>{formError}</FormError>

      {bearers.map((bearer, index) => {
        const rowErrors = errors.get(index);
        return (
          <fieldset key={index} className="grid gap-4 rounded-cup border border-hairline bg-surface p-4">
            <legend className="px-1 font-medium text-brand">{t("bearers.bearer", { number: index + 1 })}</legend>
            {fields.map((field) => (
              <Field
                key={field}
                label={t(`bearers.${field}`)}
                error={rowErrors?.has(field) ? t("fieldInvalid") : undefined}
                required={field !== "email"}
              >
                <Input
                  type={field === "phone" ? "tel" : field === "email" ? "email" : "text"}
                  inputMode={field === "phone" ? "tel" : field === "email" ? "email" : undefined}
                  autoComplete="off"
                  value={bearer[field]}
                  onChange={(event) => update(index, { [field]: event.target.value })}
                />
              </Field>
            ))}
            <label className="flex min-h-11 cursor-pointer items-center gap-3">
              <input
                type="checkbox"
                checked={bearer.isSignatory}
                onChange={(event) => update(index, { isSignatory: event.target.checked })}
                className="size-5 shrink-0 accent-[var(--color-primary)]"
              />
              <span>{t("bearers.isSignatory")}</span>
            </label>
            {bearers.length > 1 && (
              <Button variant="ghost" onClick={() => setBearers((previous) => previous.filter((_, i) => i !== index))}>
                {t("bearers.remove")}
              </Button>
            )}
          </fieldset>
        );
      })}

      {bearers.length < MAX_OFFICE_BEARERS ? (
        <Button variant="outline" onClick={() => setBearers((previous) => [...previous, emptyBearer()])}>
          {t("bearers.add")}
        </Button>
      ) : (
        <p className="text-sm text-muted">{t("bearers.limit", { count: MAX_OFFICE_BEARERS })}</p>
      )}

      <Button type="submit" size="lg" loading={busy} className="w-full">
        {t("saveContinue")}
      </Button>
      <Button variant="ghost" onClick={onBack}>
        {t("back")}
      </Button>
    </form>
  );
}
