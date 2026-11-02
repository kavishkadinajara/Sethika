"use client";

import { useLocale, useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { BookingStatusBadge } from "@/components/bookings/BookingStatusBadge";
import { useDates } from "@/components/bookings/useDates";
import { HeartIcon, InstitutionIcon, SearchIcon } from "@/components/ui/icons";
import { Link } from "@/i18n/navigation";
import { listMyBookings, listPlaces } from "@/lib/bookings/api";
import { colomboToday } from "@/lib/bookings/calendar";
import { inLocale } from "@/lib/bookings/names";
import type { DonorBooking, PublicInstitutionSummary } from "@/lib/bookings/types";
import { colomboTime, donorSentence, upcoming } from "@/lib/dashboard/sentence";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { DayHeader, Panel, PanelEmpty, PanelPlaceholder, useNow } from "./ledger-ui";
import { InvitationsCard } from "./InvitationsCard";

/** How many meals "Your table" lists, and how many places "Places you give to" names. */
const TABLE_SHOWN = 5;
const PLACES_SHOWN = 6;

/**
 * A donor's Day Ledger (plan 05, R4): the sentence names their next meal to give; "Your table"
 * lists what is coming; "Places you give to" are the institutions of their own bookings, most
 * recent first. Both come from the one read of their bookings.
 */
export function DonorDay() {
  const t = useTranslations("Dashboard");
  const locale = useLocale();
  const { can } = usePermissions();
  const now = useNow();
  const today = colomboToday(now);
  const reads = can("MYBOOKINGS.BOOKINGS.VIEW");
  const [bookings, setBookings] = useState<DonorBooking[] | null>(null);
  const [kept, setKept] = useState<PublicInstitutionSummary[] | null>(null);

  useEffect(() => {
    if (!reads) return;
    let cancelled = false;
    void listMyBookings().then((result) => {
      if (!cancelled && result.ok) setBookings(result.value);
    });
    return () => {
      cancelled = true;
    };
  }, [reads]);

  // My places (plan 07, H12): the places kept with the heart on their pages; the account's own rows.
  useEffect(() => {
    let cancelled = false;
    void listPlaces().then((result) => {
      if (!cancelled && result.ok) setKept(result.value);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const time = colomboTime(now);
  const coming = bookings ? upcoming(bookings, today, time) : null;
  const sentence = !reads ? donorSentence({ bookings: null, locale, today, now: time }) : bookings ? donorSentence({ bookings, locale, today, now: time }) : null;

  return (
    <div className="grid gap-8">
      <DayHeader context={t("asDonor")} sentence={sentence} />
      <InvitationsCard />

      <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        {reads && (
          <Panel title={t("panels.yourTable")} link={{ href: "/bookings/mine", label: t("panels.allBookings") }}>
            {coming === null ? <PanelPlaceholder /> : coming.length === 0 ? <PanelEmpty>{t("panels.nothingComing")}</PanelEmpty> : <YourTable bookings={coming.slice(0, TABLE_SHOWN)} />}
          </Panel>
        )}
        <div className="grid gap-6">
          {kept && (
            <Panel title={t("panels.myPlaces")}>
              {kept.length === 0 ? <PanelEmpty>{t("panels.noPlaces")}</PanelEmpty> : <MyPlaces places={kept} />}
            </Panel>
          )}
          {reads && bookings && bookings.length > 0 && (
            <Panel title={t("panels.places")}>
              <Places bookings={bookings} />
            </Panel>
          )}
          <Link
            href="/find"
            className={cn(
              "flex items-center gap-4 rounded-bowl border border-hairline bg-surface p-5 shadow-lift transition-colors hover:border-line",
              focusRing,
            )}
          >
            <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-primary/10 text-brand">
              <SearchIcon className="size-6" />
            </span>
            <span className="grid gap-0.5">
              <span className="text-lg font-semibold text-fg">{t("actions.find.title")}</span>
              <span className="text-sm text-muted">{t("actions.find.description")}</span>
            </span>
          </Link>
        </div>
      </div>
    </div>
  );
}

/** The meals still to give, soonest first, as lines of a ledger. */
function YourTable({ bookings }: { bookings: DonorBooking[] }) {
  const locale = useLocale();
  const dates = useDates();

  return (
    <ol className="grid grid-cols-1 divide-y divide-hairline">
      {bookings.map((b) => (
        <li key={b.bookingNo} className="grid grid-cols-[5.5rem_minmax(0,1fr)] gap-3 py-3 first:pt-0 last:pb-0">
          <span className="grid text-sm leading-tight">
            <span className="font-semibold text-fg">{dates.dayMonth(b.date)}</span>
            <span className="text-muted tabular-nums">{b.startTime}</span>
          </span>
          <span className="grid gap-1">
            <span className="text-fg">
              {inLocale(locale, b.slotName, b.slotNameSi, b.slotNameTa)} ·{" "}
              <Link href={`/i/${b.institution}`} className={cn("font-medium text-brand underline-offset-4 hover:underline", focusRing)}>
                {inLocale(locale, b.institutionName, b.institutionNameSi, b.institutionNameTa)}
              </Link>
            </span>
            <span className="flex flex-wrap items-center gap-2">
              <BookingStatusBadge status={b.status} />
              <span className="text-sm text-muted tabular-nums">{b.bookingNo}</span>
            </span>
          </span>
        </li>
      ))}
    </ol>
  );
}

/** The places the donor keeps, the newest kept first. */
function MyPlaces({ places }: { places: PublicInstitutionSummary[] }) {
  const locale = useLocale();

  return (
    <ul className="grid grid-cols-1 gap-2">
      {places.slice(0, PLACES_SHOWN).map((p) => (
        <li key={p.slug}>
          <Link
            href={`/i/${p.slug}`}
            className={cn("flex min-h-12 items-center gap-3 rounded-cup border border-hairline px-3 py-2 hover:border-line", focusRing)}
          >
            <HeartIcon filled className="size-5 shrink-0 text-accent-strong" />
            <span className="grid min-w-0">
              <span className="truncate font-medium text-fg">{inLocale(locale, p.name, p.nameSi, p.nameTa)}</span>
              <span className="truncate text-sm text-muted">
                {p.city} · {inLocale(locale, p.district, p.districtSi, p.districtTa)}
              </span>
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
}

/** The institutions of the donor's bookings, the most recently booked first. */
function Places({ bookings }: { bookings: DonorBooking[] }) {
  const locale = useLocale();
  const seen = new Map<string, DonorBooking>();
  for (const b of [...bookings].sort((x, y) => y.createdAt.localeCompare(x.createdAt))) {
    if (!seen.has(b.institution)) seen.set(b.institution, b);
  }

  return (
    <ul className="grid grid-cols-1 gap-2">
      {[...seen.values()].slice(0, PLACES_SHOWN).map((b) => (
        <li key={b.institution}>
          <Link
            href={`/i/${b.institution}`}
            className={cn("flex min-h-12 items-center gap-3 rounded-cup border border-hairline px-3 py-2 hover:border-line", focusRing)}
          >
            <InstitutionIcon className="size-5 shrink-0 text-brand" />
            <span className="min-w-0 truncate font-medium text-fg">{inLocale(locale, b.institutionName, b.institutionNameSi, b.institutionNameTa)}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
