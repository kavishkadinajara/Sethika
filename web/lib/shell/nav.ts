/**
 * An icon by name; components/shell/icons.ts draws it. A name rather than the component keeps
 * this file free of React, so it is unit-tested in node like the rest of lib/.
 */
export type NavIcon =
  | "dashboard"
  | "giving"
  | "institution"
  | "platform"
  | "search"
  | "ticket"
  | "file"
  | "calendar"
  | "bowl"
  | "inbox"
  | "key"
  | "users";

/* Each key names its label in the Shell messages (sections.*, groups.*, items.*), so a menu entry
   without a translation is a type error. */
export type SectionKey = "dashboard" | "giving" | "institution" | "platform";
export type GroupKey = "book" | "request" | "give" | "apply" | "bookings" | "services" | "giving" | "access" | "onboarding" | "administration";
export type ItemKey =
  | "find"
  | "myBookings"
  | "memorial"
  | "myServices"
  | "myGiving"
  | "needsBoard"
  | "needs"
  | "apply"
  | "calendar"
  | "slots"
  | "sponsors"
  | "donations"
  | "serviceTypes"
  | "leaders"
  | "appointments"
  | "schedule"
  | "roles"
  | "members"
  | "registrations"
  | "institutions"
  | "users";

/** A count from GET /api/me/summary (plan 05, R3) that a link may carry as a badge. */
export type BadgeKey = "bookingsToAnswer" | "registrationsWaiting" | "invitations";

/** The counts as the API sends them: null where the caller may not see the screen counted. */
export type ShellSummary = Record<BadgeKey, number | null>;

/**
 * One screen. `screen` is its code in auth.Screens (10_RbacCatalogue.sql) and the link shows when
 * the user holds `<screen>.VIEW`; a screen with no code is public (anyone signed in sees it).
 * `href` must equal the catalogue's Route, which nav.test.ts checks against the seed.
 */
export type NavItem = { key: ItemKey; href: string; icon: NavIcon; screen?: string; badge?: BadgeKey };

/** A module of the catalogue: a headed group in the sidebar. */
export type NavGroup = { key: GroupKey; items: NavItem[] };

/** A portal of the catalogue: a part of the sidebar. A section with an href is a plain link. */
export type NavSection = { key: SectionKey; icon: NavIcon; href?: string; badge?: BadgeKey; groups: NavGroup[] };

/**
 * The menu (plan 04, W2; plan 05, R2): section = portal, group = module, link = screen. Only
 * built screens are listed; add a screen here in the same change that builds it. A badge names the
 * count beside the link: invitations wait on the dashboard (InvitationsCard).
 */
export const navTree: readonly NavSection[] = [
  { key: "dashboard", icon: "dashboard", href: "/", badge: "invitations", groups: [] },
  {
    key: "giving",
    icon: "giving",
    groups: [
      {
        key: "book",
        items: [
          { key: "find", href: "/find", icon: "search" },
          { key: "myBookings", href: "/bookings/mine", icon: "ticket", screen: "MYBOOKINGS.BOOKINGS" },
          { key: "memorial", href: "/memorial", icon: "calendar", screen: "MYMEMORIAL.PLANS" },
        ],
      },
      {
        key: "request",
        items: [{ key: "myServices", href: "/services/mine", icon: "calendar", screen: "MYSERVICES.APPOINTMENTS" }],
      },
      {
        key: "give",
        items: [
          { key: "needsBoard", href: "/needs", icon: "giving" },
          { key: "myGiving", href: "/giving/mine", icon: "giving", screen: "MYGIVING.DONATIONS" },
        ],
      },
      {
        key: "apply",
        items: [{ key: "apply", href: "/apply", icon: "file", screen: "ONBOARDING.APPLICATION" }],
      },
    ],
  },
  {
    key: "institution",
    icon: "institution",
    groups: [
      {
        key: "bookings",
        items: [
          { key: "calendar", href: "/bookings/calendar", icon: "calendar", screen: "BOOKINGS.CALENDAR", badge: "bookingsToAnswer" },
          { key: "slots", href: "/bookings/slots", icon: "bowl", screen: "BOOKINGS.SLOTS" },
          { key: "sponsors", href: "/bookings/sponsors", icon: "users", screen: "BOOKINGS.SPONSORS" },
        ],
      },
      {
        key: "services",
        items: [
          { key: "appointments", href: "/services/appointments", icon: "calendar", screen: "SERVICES.APPOINTMENTS" },
          { key: "schedule", href: "/services/schedule", icon: "ticket", screen: "SERVICES.MYSCHEDULE" },
          { key: "serviceTypes", href: "/services/types", icon: "file", screen: "SERVICES.TYPES" },
          { key: "leaders", href: "/services/leaders", icon: "users", screen: "SERVICES.LEADERS" },
        ],
      },
      {
        key: "giving",
        items: [
          { key: "donations", href: "/giving/donations", icon: "giving", screen: "GIVING.DONATIONS" },
          { key: "needs", href: "/giving/needs", icon: "file", screen: "GIVING.NEEDS" },
        ],
      },
      {
        key: "access",
        items: [
          { key: "roles", href: "/access/roles", icon: "key", screen: "ACCESS.ROLES" },
          { key: "members", href: "/access/members", icon: "users", screen: "ACCESS.MEMBERS" },
        ],
      },
    ],
  },
  {
    key: "platform",
    icon: "platform",
    groups: [
      {
        key: "onboarding",
        items: [
          { key: "registrations", href: "/admin/registrations", icon: "inbox", screen: "ONBOARDING.REQUESTS", badge: "registrationsWaiting" },
        ],
      },
      {
        key: "administration",
        items: [
          { key: "institutions", href: "/admin/institutions", icon: "institution", screen: "PLATFORM.INSTITUTIONS" },
          { key: "users", href: "/admin/users", icon: "users", screen: "PLATFORM.USERS" },
        ],
      },
    ],
  },
];

/**
 * The tree as one user may see it: links whose `.VIEW` they hold, groups with a link left, and
 * sections with a group left (or an href of their own). Hiding is UX only: every endpoint keeps
 * its own [HasPermission].
 */
export function visibleNav(can: (code: string) => boolean, tree: readonly NavSection[] = navTree): NavSection[] {
  return tree.flatMap((section) => {
    const groups = section.groups.flatMap((group) => {
      const items = group.items.filter((item) => item.screen === undefined || can(`${item.screen}.VIEW`));
      return items.length > 0 ? [{ ...group, items }] : [];
    });
    return section.href !== undefined || groups.length > 0 ? [{ ...section, groups }] : [];
  });
}

/** True when `pathname` (locale stripped) is `href` or a page below it. "/" matches only itself. */
export function isActive(href: string, pathname: string): boolean {
  if (href === "/") return pathname === "/";
  return pathname === href || pathname.startsWith(`${href}/`);
}

/** The section whose screen the user is on. */
export function activeSectionKey(sections: readonly NavSection[], pathname: string): SectionKey | null {
  for (const section of sections) {
    if (section.href !== undefined && isActive(section.href, pathname)) return section.key;
    if (section.groups.some((group) => group.items.some((item) => isActive(item.href, pathname)))) return section.key;
  }
  return null;
}

/** The number a badge shows, or null for none: a count the caller may not see, or nothing waiting. */
export function badgeCount(summary: ShellSummary | null, key: BadgeKey | undefined): number | null {
  if (summary === null || key === undefined) return null;
  const count = summary[key];
  return count !== null && count > 0 ? count : null;
}

/** A place the jump palette offers: a screen the user may view, under its label in their language. */
export type JumpTarget = { key: SectionKey | ItemKey; href: string; icon: NavIcon; label: string; group: string };

/** Every link of the (already filtered) menu, once each, in menu order. */
export function jumpTargets(
  sections: readonly NavSection[],
  label: (kind: "sections" | "items" | "groups", key: string) => string,
): JumpTarget[] {
  return sections.flatMap((section) => [
    ...(section.href === undefined
      ? []
      : [{ key: section.key, href: section.href, icon: section.icon, label: label("sections", section.key), group: label("sections", section.key) }]),
    ...section.groups.flatMap((group) =>
      group.items.map((item) => ({
        key: item.key,
        href: item.href,
        icon: item.icon,
        label: label("items", item.key),
        group: label("groups", group.key),
      })),
    ),
  ]);
}

/**
 * The targets matching what was typed: every word must appear in the label or its group, case and
 * accents ignored (Sinhala and Tamil have no case and keep their marks). Labels that start with the
 * query come first; otherwise menu order holds. An empty query lists everything.
 */
export function matchJump(targets: readonly JumpTarget[], query: string): JumpTarget[] {
  const words = fold(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return [...targets];

  const matches = targets.filter((target) => {
    const text = fold(`${target.label} ${target.group}`);
    return words.every((word) => text.includes(word));
  });
  const first = fold(query.trim());
  return [
    ...matches.filter((target) => fold(target.label).startsWith(first)),
    ...matches.filter((target) => !fold(target.label).startsWith(first)),
  ];
}

/** Lower case without Latin accents; the combining marks of Sinhala and Tamil are letters, not accents. */
function fold(text: string): string {
  return text
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLocaleLowerCase();
}
