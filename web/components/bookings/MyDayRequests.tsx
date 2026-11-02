"use client";

import { useLocale, useTranslations } from "next-intl";
import { textLink } from "@/components/auth/links";
import { Link } from "@/i18n/navigation";
import { inLocale } from "@/lib/bookings/names";
import type { MyDayRequest } from "@/lib/sponsors/types";
import { cn, focusRing } from "@/lib/utils";
import { useDates } from "./useDates";

/** The days the donor asked a regular donor's place for (plan 08, K6), and the institution's answer. */
export function MyDayRequests({ requests }: { requests: MyDayRequest[] }) {
  const t = useTranslations("MyBookings.requests");
  const people = useTranslations("MyBookings");
  const locale = useLocale();
  const dates = useDates();

  return (
    <section className="grid gap-3" aria-labelledby="my-day-requests">
      <h2 id="my-day-requests" className="text-xl font-semibold text-brand">
        {t("title")}
      </h2>
      <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
        {requests.map((request) => (
          <li key={request.requestNo}>
            <article className="grid gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift" data-status={request.status}>
              <div className="flex flex-wrap items-start justify-between gap-2">
                <h3 className="text-lg font-semibold leading-snug text-fg">
                  {dates.dayMonth(request.date)} · {request.mealSlotName}
                </h3>
                <span
                  className={cn(
                    "rounded-full px-3 py-1 text-sm",
                    request.status === "Given" ? "bg-brand/10 text-brand" : request.status === "Open" ? "bg-accent/15 text-fg" : "bg-subtle text-muted",
                  )}
                >
                  {t(`status.${request.status}`)}
                </span>
              </div>
              <Link href={`/i/${request.institution}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1")}>
                {inLocale(locale, request.institutionName, request.institutionNameSi, request.institutionNameTa)}
              </Link>
              <p className="text-sm text-muted">
                {people("people", { count: request.headcount })} · {request.requestNo}
              </p>
              {request.bookingNo && <p className="text-sm text-fg">{t("booking", { bookingNo: request.bookingNo })}</p>}
              {request.declineReason && <p className="rounded-cup bg-subtle px-3 py-2 text-sm text-fg">{t("declinedBecause", { reason: request.declineReason })}</p>}
            </article>
          </li>
        ))}
      </ul>
    </section>
  );
}
