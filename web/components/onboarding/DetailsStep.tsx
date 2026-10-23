"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { AuthHeading } from "@/components/auth/AuthHeading";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { createApplication, updateApplication } from "@/lib/onboarding/api";
import { localName, mapLink } from "@/lib/onboarding/progress";
import type { ApplicationDetails, Lookups } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";
import { useOnboardingError } from "./useOnboardingError";

type Key = keyof ApplicationDetails;

/** A lookup's name in the page's language, English when it has none. */
/**
 * Step 1: the institution and the applicant. Creates the draft the first time (POST), saves it
 * after that (PUT). The API names every field it refuses, and each is marked where it is.
 */
export function DetailsStep({
  requestNo,
  initial,
  lookups,
  focusOnMount,
  onSaved,
}: {
  requestNo: string | null;
  initial: ApplicationDetails;
  lookups: Lookups;
  focusOnMount: boolean;
  onSaved: (requestNo: string, details: ApplicationDetails) => void;
}) {
  const t = useTranslations("Apply");
  const errorMessage = useOnboardingError();
  const locale = useLocale();
  const [details, setDetails] = useState(initial);
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [formError, setFormError] = useState<ReactNode>(null);
  const [busy, setBusy] = useState(false);
  const [locating, setLocating] = useState(false);

  const set = (key: Key) => (value: string) => setDetails((previous) => ({ ...previous, [key]: value }));
  const error = (key: Key) => (invalid.has(key) ? t("fieldInvalid") : undefined);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setFormError(null);
    setInvalid(new Set());

    const result = requestNo
      ? await updateApplication(requestNo, details).then((r) => (r.ok ? { ok: true as const, value: requestNo } : r))
      : await createApplication(details);

    setBusy(false);
    if (result.ok) {
      onSaved(result.value, details);
      return;
    }
    if (result.failure.fields.length > 0) {
      setInvalid(new Set(result.failure.fields));
      setFormError(t("fieldsInvalid"));
    } else {
      setFormError(errorMessage(result.failure));
    }
  }

  function useMyLocation() {
    if (!("geolocation" in navigator)) {
      setFormError(t("details.locationFailed"));
      return;
    }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLocating(false);
        // Six places is about 10 cm: as fine as the columns keep (DECIMAL(9,6)).
        setDetails((previous) => ({
          ...previous,
          latitude: position.coords.latitude.toFixed(6),
          longitude: position.coords.longitude.toFixed(6),
        }));
      },
      () => {
        setLocating(false);
        setFormError(t("details.locationFailed"));
      },
      { enableHighAccuracy: true, timeout: 15_000 },
    );
  }

  const map = mapLink(details.latitude, details.longitude);

  const text = (key: Key, options: { required?: boolean; help?: ReactNode; type?: string; inputMode?: "numeric" | "decimal" | "tel" | "email" | "url"; autoComplete?: string } = {}) => (
    <Field label={t(`details.${key}`)} description={options.help} error={error(key)} required={options.required}>
      <Input
        type={options.type ?? "text"}
        inputMode={options.inputMode}
        autoComplete={options.autoComplete ?? "off"}
        value={details[key]}
        onChange={(event) => set(key)(event.target.value)}
      />
    </Field>
  );

  return (
    <form noValidate onSubmit={save} className="grid gap-5">
      <AuthHeading title={t("details.title")} description={t("details.help")} focusOnMount={focusOnMount} />
      <FormError>{formError}</FormError>

      <Field label={t("details.institutionType")} error={error("institutionType")} required>
        <select
          className={inputClasses}
          value={details.institutionType}
          onChange={(event) => set("institutionType")(event.target.value)}
          disabled={requestNo !== null && initial.institutionType !== ""}
        >
          <option value="">{t("choose")}</option>
          {lookups.institutionTypes.map((type) => (
            <option key={type.code} value={type.code}>
              {localName(type, locale)}
            </option>
          ))}
        </select>
      </Field>

      {text("proposedName", { required: true })}
      {text("nameSi")}
      {text("nameTa")}
      {text("officialRegistrationNo", { required: true, help: t("details.officialRegistrationNoHelp") })}
      {text("registeringAuthority", { required: true, help: t("details.registeringAuthorityHelp") })}
      {text("registrationDate", { required: true, type: "date" })}
      {text("establishedYear", { inputMode: "numeric" })}
      {text("residentCapacity", { inputMode: "numeric", help: t("details.residentCapacityHelp") })}

      <Field label={t("details.description")} error={error("description")}>
        <textarea
          rows={4}
          className={cn(inputClasses, "min-h-28")}
          value={details.description}
          onChange={(event) => set("description")(event.target.value)}
        />
      </Field>

      <h2 className="mt-4 text-lg font-semibold text-brand">{t("details.addressHeading")}</h2>
      {text("addressLine1", { required: true, autoComplete: "address-line1" })}
      {text("addressLine2", { autoComplete: "address-line2" })}
      {text("city", { required: true, autoComplete: "address-level2" })}

      <Field label={t("details.district")} error={error("district")} required>
        <select className={inputClasses} value={details.district} onChange={(event) => set("district")(event.target.value)}>
          <option value="">{t("choose")}</option>
          {lookups.districts.map((district) => (
            <option key={district.code} value={district.code}>
              {localName(district, locale)}
            </option>
          ))}
        </select>
      </Field>

      {text("gnDivision")}
      {text("postalCode", { inputMode: "numeric", autoComplete: "postal-code" })}

      <div className="grid gap-3 rounded-cup border border-hairline p-4">
        <p className="text-sm text-muted">{t("details.locationHelp")}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          {text("latitude", { required: true, inputMode: "decimal" })}
          {text("longitude", { required: true, inputMode: "decimal" })}
        </div>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <Button variant="outline" onClick={useMyLocation} loading={locating}>
            {t("details.useLocation")}
          </Button>
          {map && (
            <a href={map} target="_blank" rel="noopener noreferrer" className={cn(textLink, focusRing)}>
              {t("details.checkMap")}
            </a>
          )}
        </div>
      </div>

      <h2 className="mt-4 text-lg font-semibold text-brand">{t("details.contactHeading")}</h2>
      {text("officePhone", { required: true, type: "tel", inputMode: "tel" })}
      {text("officeEmail", { type: "email", inputMode: "email" })}
      {text("website", { type: "url", inputMode: "url" })}

      <h2 className="mt-4 text-lg font-semibold text-brand">{t("details.applicantHeading")}</h2>
      <p className="-mt-3 text-sm text-muted">{t("details.applicantHelp")}</p>
      {text("applicantFullName", { required: true, autoComplete: "name" })}
      {text("applicantNic", { required: true })}
      {text("applicantDesignation", { required: true, help: t("details.applicantDesignationHelp") })}
      {text("applicantPhone", { required: true, type: "tel", inputMode: "tel", autoComplete: "tel" })}
      {text("applicantEmail", { type: "email", inputMode: "email", autoComplete: "email" })}

      <Button type="submit" size="lg" loading={busy} className="w-full">
        {t("saveContinue")}
      </Button>
    </form>
  );
}
