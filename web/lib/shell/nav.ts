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

/** A module of the catalogue. The sidebar lists its screens under the section, without a heading of its own. */
export type NavGroup = { key: GroupKey; items: NavItem[] };

/** A portal of the catalogue: a named section of the sidebar. A section with an href is a plain link. */
export type NavSection = { key: SectionKey; icon: NavIcon; href?: string; groups: NavGroup[] };

/**
 * The menu (plan 04, W2): section = portal, group = module, link = screen. Only built
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

/**
 * The href of the menu link for the page the user is on, for the sidebar's glide pill: the
 * longest one that matches, so a page below a link marks that link. Null off the menu.
 */
export function activeHref(sections: readonly NavSection[], pathname: string): string | null {
  const hrefs = sections.flatMap((section) => [
    ...(section.href !== undefined ? [section.href] : []),
    ...section.groups.flatMap((group) => group.items.map((item) => item.href)),
  ]);
  return hrefs.filter((href) => isActive(href, pathname)).sort((a, b) => b.length - a.length)[0] ?? null;
}
