import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { activeHref, isActive, navTree, visibleNav, type NavSection } from "./nav";

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

  it("drops a link whose VIEW is missing, and a group left empty by that", () => {
    const noApplying = holding("MYBOOKINGS.BOOKINGS.VIEW");
    const giving = visibleNav(noApplying).find((s) => s.key === "giving");
    expect(giving?.groups.map((g) => g.key)).toEqual(["book"]);
  });

  it("shows an institution's screens only for the VIEW actually held", () => {
    const staff = holding("BOOKINGS.CALENDAR.VIEW");
    expect(keys(visibleNav(staff))).toContain("institution[calendar]");
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

  it("finds the menu link of the page the user is on", () => {
    const all = visibleNav(() => true);
    expect(activeHref(all, "/bookings/calendar")).toBe("/bookings/calendar");
    expect(activeHref(all, "/bookings/mine")).toBe("/bookings/mine");
    expect(activeHref(all, "/admin/registrations/REQ-2026-000001")).toBe("/admin/registrations");
    expect(activeHref(all, "/")).toBe("/");
    expect(activeHref(all, "/account/password")).toBeNull();
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
