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
export type GroupKey = "book" | "apply" | "bookings" | "access" | "onboarding" | "administration";
export type ItemKey =
  | "find"
  | "myBookings"
  | "apply"
  | "calendar"
  | "slots"
  | "roles"
  | "members"
  | "registrations"
  | "institutions"
  | "users";

/**
 * One screen. `screen` is its code in auth.Screens (10_RbacCatalogue.sql) and the link shows when
 * the user holds `<screen>.VIEW`; a screen with no code is public (anyone signed in sees it).
 * `href` must equal the catalogue's Route, which nav.test.ts checks against the seed.
 */
export type NavItem = { key: ItemKey; href: string; icon: NavIcon; screen?: string };

/** A module of the catalogue: a group in the flyout's accordion. */
export type NavGroup = { key: GroupKey; items: NavItem[] };

/** A portal of the catalogue: one icon on the rail. A section with an href is a plain link. */
export type NavSection = { key: SectionKey; icon: NavIcon; href?: string; groups: NavGroup[] };

/**
 * The menu (plan 04, W2): rail section = portal, group = module, link = screen. Only built
 * screens are listed; add a screen here in the same change that builds it.
 */
export const navTree: readonly NavSection[] = [
  { key: "dashboard", icon: "dashboard", href: "/", groups: [] },
  {
    key: "giving",
    icon: "giving",
    groups: [
      {
        key: "book",
        items: [
          { key: "find", href: "/institutions", icon: "search" },
          { key: "myBookings", href: "/bookings/mine", icon: "ticket", screen: "MYBOOKINGS.BOOKINGS" },
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
          { key: "calendar", href: "/bookings/calendar", icon: "calendar", screen: "BOOKINGS.CALENDAR" },
          { key: "slots", href: "/bookings/slots", icon: "bowl", screen: "BOOKINGS.SLOTS" },
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
          { key: "registrations", href: "/admin/registrations", icon: "inbox", screen: "ONBOARDING.REQUESTS" },
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

/** The section whose screen the user is on, for the rail's highlight. */
export function activeSectionKey(sections: readonly NavSection[], pathname: string): SectionKey | null {
  for (const section of sections) {
    if (section.href !== undefined && isActive(section.href, pathname)) return section.key;
    if (section.groups.some((group) => group.items.some((item) => isActive(item.href, pathname)))) return section.key;
  }
  return null;
}
