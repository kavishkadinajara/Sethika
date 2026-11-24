"use client";

import { useLocale, useTranslations } from "next-intl";
import { textLink } from "@/components/auth/links";
import { Link } from "@/i18n/navigation";
import { asDate } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import type { SharedBooking } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";
import { BookingStatusBadge } from "./BookingStatusBadge";
import { useDates } from "./useDates";

/**
 * "Shared with me" in My bookings (plan 07, H19): the bookings this person joined through a link,
 * whose they are and what became of them. The donor's booking, not theirs: no cancel here.
 */
export function SharedWithMe({ shared }: { shared: SharedBooking[] }) {
  const t = useTranslations("MyBookings.shared");
  const people = useTranslations("MyBookings");
  const locale = useLocale();
  const dates = useDates();

  return (
    <section className="grid gap-3" aria-labelledby="shared-with-me">
      <h2 id="shared-with-me" className="text-xl font-semibold text-brand">
        {t("title")}
      </h2>
      <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
        {shared.map((b) => (
          <li key={b.bookingNo}>
            <article className="flex overflow-hidden rounded-bowl border border-hairline bg-surface shadow-lift">
              <div className="grid w-20 shrink-0 content-center justify-items-center gap-0.5 border-r border-dashed border-hairline bg-accent/10 px-2 py-4 text-center text-fg">
                <span className="text-sm font-medium">{dates.weekday(b.date, "short")}</span>
                <span className="text-3xl font-semibold leading-none tabular-nums">{asDate(b.date).getUTCDate()}</span>
                <span className="text-sm">{dates.monthShort(b.date)}</span>
              </div>
              <div className="grid min-w-0 flex-1 gap-1.5 p-4">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <h3 className="min-w-0 text-lg font-semibold leading-snug text-fg">
                    {inLocale(locale, b.mealName, b.mealNameSi, b.mealNameTa)}
                    <span className="font-normal text-muted"> · {b.startTime}</span>
                  </h3>
                  <BookingStatusBadge status={b.status} />
                </div>
                <Link href={`/i/${b.institutionSlug}`} className={cn(textLink, focusRing, "min-h-0 justify-self-start py-1")}>
                  {inLocale(locale, b.institutionName, b.institutionNameSi, b.institutionNameTa)}
                </Link>
                <p className="text-sm text-muted">
                  {t("with", { name: b.donorName })} · {people("people", { count: b.headcount })}
                </p>
                {b.inMemoryOf && <p className="font-tagline text-base italic text-fg">{people("inMemoryOf", { name: b.inMemoryOf })}</p>}
              </div>
            </article>
          </li>
        ))}
      </ul>
    </section>
  );
}
