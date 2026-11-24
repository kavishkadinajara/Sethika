"use client";

import { useTranslations } from "next-intl";
import { useEffect, useId, useState, type ReactNode } from "react";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { useProfile } from "@/components/shell/useProfile";
import { Card } from "@/components/ui/card";
import { Link } from "@/i18n/navigation";
import { COLOMBO, colomboToday } from "@/lib/bookings/calendar";
import type { Sentence } from "@/lib/dashboard/sentence";
import { cn, focusRing } from "@/lib/utils";

/*  The Day Ledger's shared frame (plan 05, R4): a large italic date beside the one sentence that
    says what today needs, then the panels of the session's kind. Bowl-radius cards on the R1
    surfaces; no brand panel.                                                                    */

/** The date, who the session acts for, and today's sentence; `sentence` null while its data loads. */
export function DayHeader({ context, sentence }: { context: string; sentence: Sentence | null }) {
  const t = useTranslations("Dashboard");
  const dates = useDates();
  const profile = useProfile();
  const today = colomboToday(useNow());
  const name = profile?.displayName || profile?.fullName;

  return (
    <header className="grid gap-6 border-b border-hairline pb-8 lg:grid-cols-[minmax(0,auto)_minmax(0,1fr)] lg:items-end lg:gap-10">
      <div className="grid gap-2">
        <p className="text-base text-muted">{name ? t("welcome", { name }) : t("welcomeNoName")}</p>
        {/* Line heights with room above: Sinhala's tall vowel signs ran into the line above at 0.95. */}
        <h1 className="flex flex-col gap-2 font-tagline text-brand italic">
          <span className="text-2xl leading-snug sm:text-3xl">{dates.weekday(today, "long")}</span>{" "}
          <span className="text-5xl leading-[1.25] sm:text-7xl">{dates.dayMonthLong(today)}</span>
        </h1>
      </div>
      <div className="grid gap-2 lg:border-l lg:border-hairline lg:pl-10">
        <p className="font-tagline text-lg text-muted italic">{context}</p>
        {/* Polite: the sentence changes when a decision changes the day, and that is worth hearing. */}
        <p aria-live="polite" className="max-w-3xl text-xl leading-snug text-fg sm:text-2xl">
          {sentence ? <SentenceText sentence={sentence} /> : <span className="block h-8 w-3/4 animate-pulse rounded-full bg-subtle motion-reduce:animate-none" />}
        </p>
      </div>
    </header>
  );
}

function SentenceText({ sentence }: { sentence: Sentence }) {
  const t = useTranslations("Dashboard.sentence");
  const dates = useDates();
  const values = { ...sentence.values };
  if (typeof values.date === "string") values.date = dates.full(values.date);
  return <>{t(sentence.key as Parameters<typeof t>[0], values)}</>;
}

/** One panel: a titled bowl card, with an optional link to the screen it summarises. */
export function Panel({
  title,
  link,
  className,
  children,
}: {
  title: string;
  link?: { href: string; label: string };
  className?: string;
  children: ReactNode;
}) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={cn("min-w-0", className)}>
      <Card className="gap-4 p-5 sm:p-6">
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          <h2 id={id} className="text-xl font-semibold text-brand">
            {title}
          </h2>
          {link && (
            <Link href={link.href} className={cn(textLink, focusRing, "min-h-0 text-sm")}>
              {link.label}
            </Link>
          )}
        </div>
        {children}
      </Card>
    </section>
  );
}

/** A panel's body while it loads: no words to read, nothing to announce. */
export function PanelPlaceholder({ rows = 3 }: { rows?: number }) {
  return (
    <div aria-hidden className="grid gap-3">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="h-12 animate-pulse rounded-cup bg-subtle motion-reduce:animate-none" />
      ))}
    </div>
  );
}

/** A quiet line for a panel with nothing in it. */
export function PanelEmpty({ children }: { children: ReactNode }) {
  return <p className="font-tagline text-lg text-muted italic">{children}</p>;
}

/** "14:05" on Colombo's clock for a UTC instant. */
export function colomboClock(at: string): string {
  return new Intl.DateTimeFormat("en-GB", { timeZone: COLOMBO, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(new Date(at));
}

/** The current time, moved on every 30 seconds: the ledger's clock, no finer than a minute. */
export function useNow(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(timer);
  }, []);
  return now;
}
