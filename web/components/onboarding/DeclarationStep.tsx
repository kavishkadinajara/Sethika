"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { AuthHeading } from "@/components/auth/AuthHeading";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { CheckCircleIcon } from "@/components/ui/icons";
import { submitApplication } from "@/lib/onboarding/api";
import { documentsFor, isReadyToSubmit, missingBeforeSubmit } from "@/lib/onboarding/progress";
import type { ApplicationView, Lookups } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";
import { useOnboardingError } from "./useOnboardingError";

/**
 * Step 4: what is still missing, the institution declaration in the page's language, and submit.
 * The declaration is accepted by its content hash, so the version read is the version recorded.
 */
export function DeclarationStep({
  view,
  lookups,
  focusOnMount,
  onSubmitted,
  onBack,
}: {
  view: ApplicationView;
  lookups: Lookups;
  focusOnMount: boolean;
  onSubmitted: () => Promise<void>;
  onBack: () => void;
}) {
  const t = useTranslations("Apply");
  const errorMessage = useOnboardingError();
  const [accepted, setAccepted] = useState(false);
  const [formError, setFormError] = useState<ReactNode>(null);
  const [busy, setBusy] = useState(false);

  const missing = missingBeforeSubmit(view, lookups);
  const ready = isReadyToSubmit(missing);
  const names = new Map(documentsFor(lookups, view.institutionType).map((d) => [d.code, d.name]));
  const declaration = lookups.declaration;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy || !declaration || !ready) return;
    if (!accepted) {
      setFormError(t("declaration.mustAccept"));
      return;
    }
    setBusy(true);
    setFormError(null);
    const result = await submitApplication(view.requestNo, declaration.contentSha256);
    if (result.ok) {
      await onSubmitted();
      return;
    }
    setBusy(false);
    setFormError(errorMessage(result.failure));
  }

  return (
    <form noValidate onSubmit={submit} className="grid gap-5">
      <AuthHeading title={t("declaration.title")} description={t("declaration.help")} focusOnMount={focusOnMount} />
      <FormError>{formError}</FormError>

      {ready ? (
        <p className="flex items-center gap-2 text-brand">
          <CheckCircleIcon className="size-5 shrink-0" />
          {t("declaration.ready")}
        </p>
      ) : (
        <div className="rounded-cup border border-accent/60 bg-accent/10 p-4">
          <p className="font-medium">{t("declaration.missingHeading")}</p>
          <ul className="mt-2 list-disc ps-5">
            {missing.officeBearers && <li>{t("bearers.needOne")}</li>}
            {!missing.officeBearers && missing.signatory && <li>{t("bearers.needSignatory")}</li>}
            {missing.documents.map((code) => (
              <li key={code}>{t("declaration.missingDocument", { name: names.get(code) ?? code })}</li>
            ))}
          </ul>
        </div>
      )}

      {declaration ? (
        <fieldset className="grid gap-2 rounded-cup border border-hairline bg-surface p-4">
          <legend className="sr-only">{declaration.title}</legend>
          <details className="group">
            <summary className={cn("flex min-h-11 cursor-pointer items-center justify-between gap-3 rounded-md font-medium text-brand", focusRing)}>
              <span>{t("declaration.read", { title: declaration.title })}</span>
              <span className="text-sm font-normal text-muted">{t("declaration.version", { version: declaration.version })}</span>
            </summary>
            <div className="mt-2 max-h-72 overflow-y-auto overscroll-contain whitespace-pre-line rounded-md bg-subtle p-3 text-sm leading-relaxed">
              {declaration.content}
            </div>
          </details>
          <label className="flex min-h-11 cursor-pointer items-start gap-3 pt-1">
            <input
              type="checkbox"
              checked={accepted}
              onChange={(event) => setAccepted(event.target.checked)}
              className="mt-0.5 size-5 shrink-0 accent-[var(--color-primary)]"
            />
            <span>{t("declaration.accept", { title: declaration.title })}</span>
          </label>
        </fieldset>
      ) : (
        <FormError>{t("declaration.unavailable")}</FormError>
      )}

      <Button type="submit" size="lg" loading={busy} disabled={!ready || !declaration} className="w-full">
        {t("declaration.submit")}
      </Button>
      <Button variant="ghost" onClick={onBack}>
        {t("back")}
      </Button>
    </form>
  );
}
