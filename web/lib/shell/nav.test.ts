import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import {
  activeSectionKey,
  badgeCount,
  isActive,
  jumpTargets,
  matchJump,
  navTree,
  visibleNav,
  type NavSection,
} from "./nav";

const holding =
  (...codes: string[]) =>
  (code: string) =>
    codes.includes(code);

const keys = (sections: NavSection[]) =>
  sections.map((section) => `${section.key}[${section.groups.flatMap((g) => g.items.map((i) => i.key)).join(",")}]`);

describe("visible menu", () => {
  it("gives a donor the dashboard and giving, and nothing of an institution or the platform", () => {
    const donor = holding("MYBOOKINGS.BOOKINGS.VIEW", "ONBOARDING.APPLICATION.VIEW");
    expect(keys(visibleNav(donor))).toEqual(["dashboard[]", "giving[find,myBookings,apply]"]);
  });

  it("gives a donor who may ask for services their requests, in a group of its own", () => {
    const donor = holding("MYBOOKINGS.BOOKINGS.VIEW", "MYSERVICES.APPOINTMENTS.VIEW");
    const giving = visibleNav(donor).find((s) => s.key === "giving");
    expect(giving?.groups.map((g) => `${g.key}[${g.items.map((i) => i.key).join(",")}]`)).toEqual(["book[find,myBookings]", "request[myServices]"]);
  });

  it("drops a link whose VIEW is missing, and a group left empty by that", () => {
    const noApplying = holding("MYBOOKINGS.BOOKINGS.VIEW");
    const giving = visibleNav(noApplying).find((s) => s.key === "giving");
    expect(giving?.groups.map((g) => g.key)).toEqual(["book"]);
  });

  it("shows an institution's screens only for the VIEW actually held", () => {
    const staff = holding("BOOKINGS.CALENDAR.VIEW");
    expect(keys(visibleNav(staff))).toContain("institution[calendar]");
  });

  it("gives a leader only their schedule, and an owner the services without the schedule", () => {
    expect(keys(visibleNav(holding("SERVICES.MYSCHEDULE.VIEW")))).toContain("institution[schedule]");
    const owner = holding("SERVICES.APPOINTMENTS.VIEW", "SERVICES.TYPES.VIEW", "SERVICES.LEADERS.VIEW");
    expect(keys(visibleNav(owner))).toContain("institution[appointments,serviceTypes,leaders]");
  });

  it("needs VIEW, not another action on the same screen", () => {
    const approverOnly = holding("BOOKINGS.CALENDAR.APPROVE", "ONBOARDING.REQUESTS.APPROVE");
    expect(keys(visibleNav(approverOnly))).toEqual(["dashboard[]", "giving[find]"]);
  });

  it("gives a platform reviewer the registrations", () => {
    expect(keys(visibleNav(holding("ONBOARDING.REQUESTS.VIEW")))).toContain("platform[registrations]");
  });

  it("keeps the public links for someone holding nothing", () => {
    expect(keys(visibleNav(holding()))).toEqual(["dashboard[]", "giving[find]"]);
  });
});

describe("active link", () => {
  it("matches a page and the pages below it, but not a sibling that shares a prefix", () => {
    expect(isActive("/apply", "/apply")).toBe(true);
    expect(isActive("/apply", "/apply/REQ-2026-000001")).toBe(true);
    expect(isActive("/apply", "/applying")).toBe(false);
  });

  it("treats the dashboard as itself only", () => {
    expect(isActive("/", "/")).toBe(true);
    expect(isActive("/", "/apply")).toBe(false);
  });

  it("finds the section of the screen the user is on", () => {
    const all = visibleNav(() => true);
    expect(activeSectionKey(all, "/bookings/calendar")).toBe("institution");
    expect(activeSectionKey(all, "/bookings/mine")).toBe("giving");
    expect(activeSectionKey(all, "/admin/registrations/REQ-2026-000001")).toBe("platform");
    expect(activeSectionKey(all, "/")).toBe("dashboard");
    expect(activeSectionKey(all, "/account/password")).toBeNull();
  });
});

describe("the menu agrees with the RBAC catalogue (10_RbacCatalogue.sql)", () => {
  const seed = readFileSync(
    fileURLToPath(new URL("../../../src/Sethika.Database/Scripts/Seed/10_RbacCatalogue.sql", import.meta.url)),
    "utf8",
  );

  // ('BOOKINGS', 'BOOKINGS.CALENDAR', N'Booking calendar', '/bookings/calendar', 1),
  const routes = new Map(
    [...seed.matchAll(/\('\w+',\s*'([A-Z]+\.[A-Z]+)',\s*N'[^']*',\s*'(\/[^']*)',\s*\d+\)/g)].map((m) => [m[1], m[2]]),
  );
  const items = navTree.flatMap((section) => section.groups.flatMap((group) => group.items));

  it("reads the screens from the seed", () => {
    expect(routes.get("BOOKINGS.CALENDAR")).toBe("/bookings/calendar");
  });

  it.each(items.filter((item) => item.screen !== undefined).map((item) => [item.screen!, item.href]))(
    "%s is a catalogue screen at %s with a VIEW permission",
    (screen, href) => {
      expect(routes.get(screen)).toBe(href);
      expect(seed).toContain(`('${screen}.VIEW',`);
    },
  );
});

describe("badges", () => {
  const summary = { bookingsToAnswer: 3, registrationsWaiting: 0, invitations: null };

  it("shows a count only when something waits", () => {
    expect(badgeCount(summary, "bookingsToAnswer")).toBe(3);
    expect(badgeCount(summary, "registrationsWaiting")).toBeNull();
  });

  it("shows nothing for a count the caller may not see, or before the counts arrive", () => {
    expect(badgeCount(summary, "invitations")).toBeNull();
    expect(badgeCount(null, "bookingsToAnswer")).toBeNull();
    expect(badgeCount(summary, undefined)).toBeNull();
  });

  it("puts each count on the screen it counts", () => {
    const badges = navTree.flatMap((s) => [
      ...(s.badge ? [`${s.key}:${s.badge}`] : []),
      ...s.groups.flatMap((g) => g.items.filter((i) => i.badge).map((i) => `${i.key}:${i.badge}`)),
    ]);
    expect(badges).toEqual(["dashboard:invitations", "calendar:bookingsToAnswer", "registrations:registrationsWaiting"]);
  });
});

describe("jump to", () => {
  const english: Record<string, string> = {
    "sections.dashboard": "Dashboard",
    "items.find": "Find a place to give",
    "items.myBookings": "My bookings",
    "items.calendar": "Booking calendar",
    "items.slots": "Meals and days",
    "groups.book": "Meals",
    "groups.bookings": "Bookings",
  };
  const label = (kind: string, key: string) => english[`${kind}.${key}`] ?? `${kind}.${key}`;
  const targets = jumpTargets(
    visibleNav(holding("MYBOOKINGS.BOOKINGS.VIEW", "BOOKINGS.CALENDAR.VIEW", "BOOKINGS.SLOTS.VIEW")),
    label,
  );
  const found = (query: string) => matchJump(targets, query).map((t) => t.key);

  it("offers every screen the user may view, once, in menu order", () => {
    expect(targets.map((t) => t.key)).toEqual(["dashboard", "find", "myBookings", "calendar", "slots"]);
    expect(found("")).toEqual(["dashboard", "find", "myBookings", "calendar", "slots"]);
  });

  it("never offers a screen the menu hides", () => {
    expect(found("role")).toEqual([]);
    expect(found("registration")).toEqual([]);
  });

  it("matches every word, in the label or its group, whatever the case", () => {
    expect(found("BOOKING")).toEqual(["calendar", "myBookings", "slots"]);
    expect(found("cal book")).toEqual(["calendar"]);
  });

  it("puts labels that start with the query first", () => {
    expect(found("meals")).toEqual(["slots", "find", "myBookings"]);
  });

  it("ignores Latin accents, and leaves Sinhala marks alone", () => {
    const sinhala = matchJump([{ ...targets[3]!, label: "වෙන්කිරීම් දින දර්ශනය", group: "වෙන්කිරීම්" }], "දින");
    expect(sinhala).toHaveLength(1);
    expect(matchJump([{ ...targets[0]!, label: "Café" }], "cafe")).toHaveLength(1);
  });
});
