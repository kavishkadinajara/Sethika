"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState, type ComponentType, type SVGProps } from "react";
import { useDates } from "@/components/bookings/useDates";
import { useProfile } from "@/components/shell/useProfile";
import {
  BowlIcon,
  CalendarIcon,
  ChevronRightIcon,
  FileTextIcon,
  InboxIcon,
  InstitutionIcon,
  KeyIcon,
  LockIcon,
  SearchIcon,
  TicketIcon,
  UsersIcon,
} from "@/components/ui/icons";
import { Link } from "@/i18n/navigation";
import { COLOMBO, colomboToday } from "@/lib/bookings/calendar";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { partOfDay } from "@/lib/shell/greeting";
import { cn, focusRing } from "@/lib/utils";
import { InvitationsCard } from "./InvitationsCard";

type Action = {
  key:
    | "registrations"
    | "institutions"
    | "users"
    | "calendar"
    | "slots"
    | "roles"
    | "members"
    | "find"
    | "myBookings"
    | "apply"
    | "password";
  href: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  /** Shown when held; none means everyone signed in. Hiding is UX only (plan 04, W2). */
  permission?: string;
};

/* The work of the session's role first: the platform's queue, then the institution's day, then
   the donor's own; the password last, for everyone. */
const actions: Action[] = [
  { key: "registrations", href: "/admin/registrations", icon: InboxIcon, permission: "ONBOARDING.REQUESTS.VIEW" },
  { key: "institutions", href: "/admin/institutions", icon: InstitutionIcon, permission: "PLATFORM.INSTITUTIONS.VIEW" },
  { key: "users", href: "/admin/users", icon: UsersIcon, permission: "PLATFORM.USERS.VIEW" },
  { key: "calendar", href: "/bookings/calendar", icon: CalendarIcon, permission: "BOOKINGS.CALENDAR.VIEW" },
  { key: "slots", href: "/bookings/slots", icon: BowlIcon, permission: "BOOKINGS.SLOTS.VIEW" },
  { key: "members", href: "/access/members", icon: UsersIcon, permission: "ACCESS.MEMBERS.VIEW" },
  { key: "roles", href: "/access/roles", icon: KeyIcon, permission: "ACCESS.ROLES.VIEW" },
  { key: "find", href: "/institutions", icon: SearchIcon },
  { key: "myBookings", href: "/bookings/mine", icon: TicketIcon, permission: "MYBOOKINGS.BOOKINGS.VIEW" },
  { key: "apply", href: "/apply", icon: FileTextIcon, permission: "ONBOARDING.APPLICATION.CREATE" },
  { key: "password", href: "/account/password", icon: LockIcon },
];

/** The signed-in home page (plan 04): a greeting in Colombo's time, and the user's way into their work. */
export function Dashboard() {
  const t = useTranslations("Dashboard");
  const { can } = usePermissions();

  return (
    <div className="grid gap-10">
      <Greeting />
      <InvitationsCard />
      <section aria-labelledby="quick-actions" className="grid gap-4">
        <h2 id="quick-actions" className="text-2xl font-semibold text-brand">
          {t("quickActions")}
        </h2>
        <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {actions
            .filter((action) => action.permission === undefined || can(action.permission))
            .map(({ key, href, icon: Icon }) => (
              <li key={key}>
                <Link
                  href={href}
                  className={cn(
                    "group flex h-full items-center gap-4 rounded-bowl border border-hairline bg-surface p-5 shadow-lift",
                    "transition-[translate,border-color] duration-200 hover:-translate-y-0.5 hover:border-line motion-reduce:hover:translate-y-0",
                    focusRing,
                  )}
                >
                  <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-primary/10 text-brand">
                    <Icon className="size-6" />
                  </span>
                  <span className="grid min-w-0 flex-1 gap-0.5">
                    <span className="text-lg leading-snug font-semibold text-fg group-hover:text-brand">{t(`actions.${key}.title`)}</span>
                    <span className="text-sm text-muted">{t(`actions.${key}.description`)}</span>
                  </span>
                  <ChevronRightIcon className="size-5 shrink-0 text-muted transition-transform group-hover:translate-x-0.5" />
                </Link>
              </li>
            ))}
        </ul>
      </section>
    </div>
  );
}

/** "Good afternoon!", who the session acts for, and today's date and time in Colombo. */
function Greeting() {
  const t = useTranslations("Dashboard");
  const tShell = useTranslations("Shell");
  const state = useSession();
  const profile = useProfile();
  const { isPlatform } = usePermissions();
  const dates = useDates();
  const now = useNow();

  const name = profile?.displayName || profile?.fullName;
  const institution =
    state.status === "authenticated" ? state.institutions.find((i) => i.slug === state.activeInstitution) : undefined;
  const role = institution
    ? t("asInstitution", { name: institution.name })
    : isPlatform
      ? t("asPlatform")
      : t("asDonor");
  const time = new Intl.DateTimeFormat("en-GB", { timeZone: COLOMBO, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(now);

  return (
    <section
      className={cn(
        "relative overflow-hidden rounded-bowl p-6 text-ivory shadow-lift sm:p-8",
        // A brand panel (DESIGN.md): Estate Green in both themes, a tonal gradient of its own green.
        "bg-[linear-gradient(135deg,#1f7356_0%,var(--color-estate)_50%,#0f3d2e_100%)]",
        "before:absolute before:-top-px before:left-6 before:h-[3px] before:w-14 before:rounded-b-full before:bg-accent sm:before:left-8",
      )}
    >
      <svg aria-hidden viewBox="0 0 28 19" className="pointer-events-none absolute -right-8 -bottom-16 w-80 text-white/6" fill="currentColor">
        <path d="M2 5h24a12 11 0 0 1-24 0z" />
      </svg>
      <div className="relative flex flex-wrap items-end justify-between gap-6">
        <div className="grid gap-2">
          <h1 className="text-3xl leading-tight font-semibold sm:text-4xl">{t(partOfDay(now))}!</h1>
          <p className="text-lg text-ivory/85">{name ? t("welcome", { name }) : t("welcomeNoName")}</p>
          <p className="font-tagline text-lg text-ivory/90 italic">{role}</p>
          {institution === undefined && (
            <p className="sr-only">
              {tShell("context.label")}: {isPlatform ? tShell("context.platform") : tShell("context.donor")}
            </p>
          )}
        </div>
        <div className="grid justify-items-start gap-1 sm:justify-items-end">
          <span className="font-tagline text-5xl leading-none tabular-nums sm:text-6xl">{time}</span>
          <span className="text-base text-ivory/80">{dates.full(colomboToday(now))}</span>
        </div>
      </div>
    </section>
  );
}

/** The current time, moved on every 30 seconds: the greeting's clock, no finer than a minute. */
function useNow(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(timer);
  }, []);
  return now;
}
