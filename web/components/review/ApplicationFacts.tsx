"use client";

import { useFormatter, useLocale, useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { textLink } from "@/components/auth/links";
import { localName, mapLink } from "@/lib/onboarding/progress";
import type { ApplicationView, Lookups } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";

/**
 * What the applicant said about the institution and themselves, to read and check against the
 * documents. The labels are the applicant form's own (Apply.details), so both sides name a field
 * the same way.
 */
export function ApplicationFacts({ view, lookups }: { view: ApplicationView; lookups: Lookups }) {
  const t = useTranslations("Review.detail");
  const label = useTranslations("Apply.details");
  const bearer = useTranslations("Apply.bearers");
  const format = useFormatter();
  const locale = useLocale();

  const type = lookups.institutionTypes.find((x) => x.code === view.institutionType);
  const district = lookups.districts.find((x) => x.code === view.district);
  const map = mapLink(view.latitude, view.longitude);

  return (
    <div className="grid gap-6">
      <Facts title={t("institution")}>
        <Fact label={label("institutionType")}>{type ? localName(type, locale) : view.institutionType}</Fact>
        <Fact label={label("proposedName")}>{view.proposedName}</Fact>
        <Fact label={label("nameSi")}>{view.nameSi}</Fact>
        <Fact label={label("nameTa")}>{view.nameTa}</Fact>
        <Fact label={label("officialRegistrationNo")}>{view.officialRegistrationNo}</Fact>
        <Fact label={label("registeringAuthority")}>{view.registeringAuthority}</Fact>
        <Fact label={label("registrationDate")}>{format.dateTime(new Date(view.registrationDate), { dateStyle: "medium" })}</Fact>
        <Fact label={label("establishedYear")}>{view.establishedYear}</Fact>
        <Fact label={label("residentCapacity")}>{view.residentCapacity}</Fact>
        <Fact label={label("description")}>{view.description}</Fact>
      </Facts>

      <Facts title={t("location")}>
        <Fact label={label("addressLine1")}>{view.addressLine1}</Fact>
        <Fact label={label("addressLine2")}>{view.addressLine2}</Fact>
        <Fact label={label("city")}>{view.city}</Fact>
        <Fact label={label("district")}>{district ? localName(district, locale) : view.district}</Fact>
        <Fact label={label("gnDivision")}>{view.gnDivision}</Fact>
        <Fact label={label("postalCode")}>{view.postalCode}</Fact>
        <Fact label={t("coordinates")}>
          {view.latitude}, {view.longitude}
          {map && (
            <>
              {" · "}
              <a href={map} target="_blank" rel="noopener noreferrer" className={cn(textLink, focusRing, "min-h-0")}>
                {label("checkMap")}
              </a>
            </>
          )}
        </Fact>
      </Facts>

      <Facts title={t("contact")}>
        <Fact label={label("officePhone")}>{view.officePhone}</Fact>
        <Fact label={label("officeEmail")}>{view.officeEmail}</Fact>
        <Fact label={label("website")}>{view.website}</Fact>
      </Facts>

      <Facts title={t("applicant")}>
        <Fact label={label("applicantFullName")}>{view.applicantFullName}</Fact>
        <Fact label={label("applicantNic")}>{view.applicantNic}</Fact>
        <Fact label={label("applicantDesignation")}>{view.applicantDesignation}</Fact>
        <Fact label={label("applicantPhone")}>{view.applicantPhone}</Fact>
        <Fact label={label("applicantEmail")}>{view.applicantEmail}</Fact>
      </Facts>

      <section className="grid gap-3">
        <h2 className="text-lg font-semibold text-brand">{t("bearers")}</h2>
        <ul className="grid gap-3">
          {view.officeBearers.map((b, index) => (
            <li key={index} className="grid gap-1 rounded-cup border border-hairline bg-surface p-4 text-sm">
              <p className="font-medium text-fg">
                {b.fullName} · {b.designation}
              </p>
              <p className="text-muted">
                {bearer("nic")}: {b.nic} · {bearer("phone")}: {b.phone}
                {b.email && ` · ${b.email}`}
              </p>
              {b.isSignatory && <p className="text-brand">{t("signatory")}</p>}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

function Facts({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="grid gap-3">
      <h2 className="text-lg font-semibold text-brand">{title}</h2>
      <dl className="grid gap-x-4 gap-y-3 rounded-cup border border-hairline bg-surface p-4 text-sm sm:grid-cols-[minmax(0,12rem)_1fr]">
        {children}
      </dl>
    </section>
  );
}

/** One fact; a blank answer is said to be blank rather than left out, so a missing field is noticed. */
function Fact({ label, children }: { label: string; children: ReactNode }) {
  const t = useTranslations("Review.detail");
  const blank = children === null || children === undefined || children === "";
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className={cn("-mt-2 break-words sm:mt-0", blank ? "text-muted italic" : "text-fg")}>{blank ? t("notGiven") : children}</dd>
    </>
  );
}
