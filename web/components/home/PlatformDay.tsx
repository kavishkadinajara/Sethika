"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { useDates } from "@/components/bookings/useDates";
import { Link } from "@/i18n/navigation";
import { getInstitutionStatuses, listDecided } from "@/lib/admin/api";
import type { InstitutionStatusCount, RegistrationDecision } from "@/lib/admin/types";
import { colomboToday, daysBetween } from "@/lib/bookings/calendar";
import { platformSentence } from "@/lib/dashboard/sentence";
import { listForReview } from "@/lib/onboarding/review-api";
import type { ReviewQueuePage } from "@/lib/onboarding/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { colomboClock, DayHeader, Panel, PanelEmpty, PanelPlaceholder, useNow } from "./ledger-ui";
import { InvitationsCard } from "./InvitationsCard";

/** How many of the oldest waiting applications the queue panel lists. */
const QUEUE_SHOWN = 5;

/** Days an application has waited since it was submitted, by Colombo's calendar. */
const waited = (submittedAt: string | null, today: string) => (submittedAt ? Math.max(0, daysBetween(colomboToday(new Date(submittedAt)), today)) : 0);

/**
 * The platform's Day Ledger (plan 05, R4): the review queue oldest first with each one's age,
 * the institutions by status, and the ledger of the latest decisions. Each panel is behind its
 * screen's permission, and its query is not sent without it.
 */
export function PlatformDay() {
  const t = useTranslations("Dashboard");
  const { can } = usePermissions();
  const today = colomboToday(useNow());
  const reviews = can("ONBOARDING.REQUESTS.VIEW");
  const institutions = can("PLATFORM.INSTITUTIONS.VIEW");

  const [queue, setQueue] = useState<ReviewQueuePage | null>(null);
  const [statuses, setStatuses] = useState<InstitutionStatusCount[] | null>(null);
  const [decided, setDecided] = useState<RegistrationDecision[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    if (reviews) {
      void listForReview(null, 1).then((r) => !cancelled && r.ok && setQueue(r.value));
      void listDecided().then((r) => !cancelled && r.ok && setDecided(r.value));
    }
    if (institutions) void getInstitutionStatuses().then((r) => !cancelled && r.ok && setStatuses(r.value));
    return () => {
      cancelled = true;
    };
  }, [reviews, institutions]);

  const oldest = queue?.items[0];
  const sentence = !reviews
    ? platformSentence({ waiting: null, oldestDays: null })
    : queue
      ? platformSentence({ waiting: queue.total, oldestDays: oldest ? waited(oldest.submittedAt, today) : null })
      : null;

  return (
    <div className="grid gap-8">
      <DayHeader context={t("asPlatform")} sentence={sentence} />
      <InvitationsCard />

      <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <div className="grid min-w-0 gap-6">
          {reviews && (
            <Panel
              title={t("panels.queue")}
              link={{ href: "/admin/registrations", label: queue ? t("panels.allQueue", { count: queue.total }) : t("panels.openQueue") }}
            >
              {queue === null ? (
                <PanelPlaceholder />
              ) : queue.items.length === 0 ? (
                <PanelEmpty>{t("panels.queueClear")}</PanelEmpty>
              ) : (
                <ol className="grid grid-cols-1 gap-2">
                  {queue.items.slice(0, QUEUE_SHOWN).map((item) => (
                    <li key={item.requestNo}>
                      <Link
                        href={`/admin/registrations/${item.requestNo}`}
                        className={cn("flex min-h-12 items-center gap-3 rounded-cup border border-hairline px-3 py-2 hover:border-line", focusRing)}
                      >
                        <span className="grid min-w-0 flex-1 leading-snug">
                          <span className="truncate font-medium text-fg">{item.proposedName}</span>
                          <span className="text-sm text-muted tabular-nums">{item.requestNo}</span>
                        </span>
                        <span className="shrink-0 rounded-full bg-subtle px-3 py-0.5 text-sm text-fg tabular-nums">
                          {t("panels.waited", { days: waited(item.submittedAt, today) })}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ol>
              )}
            </Panel>
          )}
          {reviews && (
            <Panel title={t("panels.decisions")}>
              {decided === null ? <PanelPlaceholder /> : decided.length === 0 ? <PanelEmpty>{t("panels.noDecisions")}</PanelEmpty> : <Decisions items={decided} today={today} />}
            </Panel>
          )}
        </div>
        {institutions && (
          <Panel title={t("panels.institutions")} link={{ href: "/admin/institutions", label: t("panels.openInstitutions") }}>
            {statuses === null ? <PanelPlaceholder rows={4} /> : <Statuses counts={statuses} />}
          </Panel>
        )}
      </div>
    </div>
  );
}

/** The institutions in each status, with a bar as long as its share. */
function Statuses({ counts }: { counts: InstitutionStatusCount[] }) {
  const t = useTranslations("Dashboard.status");
  const total = counts.reduce((sum, c) => sum + c.count, 0);

  return (
    <dl className="grid gap-3">
      {counts.map((c) => (
        <div key={c.status} className="grid gap-1.5">
          <div className="flex items-baseline justify-between gap-3">
            <dt className="text-fg">{t(c.status)}</dt>
            <dd className="text-lg font-semibold text-fg tabular-nums">{c.count}</dd>
          </div>
          <div aria-hidden className="h-1.5 overflow-hidden rounded-full bg-subtle">
            <div
              style={{ width: `${total ? (c.count / total) * 100 : 0}%` }}
              className={cn("h-full rounded-full", c.status === "Active" ? "bg-primary" : c.status === "Suspended" ? "bg-danger" : "bg-line")}
            />
          </div>
        </div>
      ))}
    </dl>
  );
}

/** The latest approvals and rejections, newest first. */
function Decisions({ items, today }: { items: RegistrationDecision[]; today: string }) {
  const t = useTranslations("Dashboard.decision");
  const dates = useDates();

  return (
    <ol className="grid grid-cols-1 divide-y divide-hairline">
      {items.map((d) => {
        const on = colomboToday(new Date(d.decidedAt));
        return (
          <li key={d.requestNo} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-3 py-3 first:pt-0 last:pb-0">
            <span className="text-sm text-muted tabular-nums">{on === today ? colomboClock(d.decidedAt) : dates.dayMonth(on)}</span>
            <span className="grid gap-0.5">
              <span className="text-fg">{t(d.status, { name: d.proposedName })}</span>
              <span className="text-sm text-muted tabular-nums">
                {d.institution ? (
                  <Link href={`/i/${d.institution}`} className={cn("text-brand underline-offset-4 hover:underline", focusRing)}>
                    {d.requestNo}
                  </Link>
                ) : (
                  d.requestNo
                )}
              </span>
            </span>
          </li>
        );
      })}
    </ol>
  );
}
