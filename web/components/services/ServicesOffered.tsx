"use client";

import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { localName } from "@/lib/bookings/names";
import type { PublicInstitution } from "@/lib/bookings/types";
import type { PublicServiceType } from "@/lib/services/types";
import { ServiceRequestSheet } from "./ServiceRequestSheet";
import { useDuration } from "./useDuration";

/**
 * The religious services an institution takes requests for (Phase E, promise 1), beside its meals
 * on its own page: what each is, how long it takes, where it can be held. Asking opens the request
 * sheet; `?service=` in the address (a donor back from signing in) opens it again.
 */
export function ServicesOffered({ institution }: { institution: PublicInstitution }) {
  const t = useTranslations("Services.offered");
  const locale = useLocale();
  const duration = useDuration();
  const search = useSearchParams();
  const [asking, setAsking] = useState<PublicServiceType | null>(
    () => institution.services.find((s) => s.publicId === search.get("service")) ?? null,
  );

  if (institution.services.length === 0) return null;

  return (
    <section className="grid gap-3" aria-labelledby="services">
      <h2 id="services" className="text-xl font-semibold text-brand">
        {t("title")}
      </h2>
      <p className="max-w-prose text-muted">{t("intro")}</p>

      <ul className="grid gap-3 sm:grid-cols-2">
        {institution.services.map((service) => (
          <li key={service.publicId}>
            <article className="grid h-full content-start gap-2 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
              <h3 className="text-lg font-semibold leading-snug text-brand">{localName(locale, service)}</h3>
              <p className="text-base text-fg">
                {t("about", { duration: duration(service.durationMinutes) })} · {t(`where.${service.locationMode}`)}
              </p>
              <p className="text-sm text-muted">{t("advance", { min: service.minAdvanceDays })}</p>
              {service.description && <p className="text-sm text-muted">{service.description}</p>}
              <Button variant="outline" onClick={() => setAsking(service)} className="mt-1 justify-self-start">
                {t("ask")}
              </Button>
            </article>
          </li>
        ))}
      </ul>

      {asking && <ServiceRequestSheet institution={institution} service={asking} onClose={() => setAsking(null)} />}
    </section>
  );
}
