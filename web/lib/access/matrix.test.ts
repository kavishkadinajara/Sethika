import { describe, expect, it } from "vitest";
import { actionsOf, canTurnOn, changes, dependentsOf, indexCatalogue, requirementsOf, roleCodeFrom, toggle } from "./matrix";
import type { CatalogueModule } from "./types";

const p = (code: string, requires: string[] = [], requiresMfa = false) => ({
  code,
  action: code.split(".")[2],
  name: code,
  requiresMfa,
  requires,
});

// The shape of 10_RbacCatalogue.sql's dependencies: actions need their VIEW, the calendar's VIEW
// needs the slots' VIEW.
const catalogue: CatalogueModule[] = [
  {
    code: "BOOKINGS",
    name: "Bookings",
    screens: [
      {
        code: "BOOKINGS.CALENDAR",
        name: "Booking calendar",
        permissions: [
          p("BOOKINGS.CALENDAR.VIEW", ["BOOKINGS.SLOTS.VIEW"]),
          p("BOOKINGS.CALENDAR.APPROVE", ["BOOKINGS.CALENDAR.VIEW"]),
          p("BOOKINGS.CALENDAR.REJECT", ["BOOKINGS.CALENDAR.VIEW"]),
        ],
      },
      {
        code: "BOOKINGS.SLOTS",
        name: "Meal slots",
        permissions: [p("BOOKINGS.SLOTS.VIEW"), p("BOOKINGS.SLOTS.CREATE", ["BOOKINGS.SLOTS.VIEW"])],
      },
    ],
  },
  {
    code: "ACCESS",
    name: "Access",
    screens: [{ code: "ACCESS.ROLES", name: "Roles", permissions: [p("ACCESS.ROLES.VIEW"), p("ACCESS.ROLES.EDIT", ["ACCESS.ROLES.VIEW"], true)] }],
  },
];
const index = indexCatalogue(catalogue);
const set = (...codes: string[]) => new Set(codes);

describe("dependencies", () => {
  it("pulls in what a permission needs, transitively", () => {
    expect([...requirementsOf("BOOKINGS.CALENDAR.APPROVE", index)].sort()).toEqual([
      "BOOKINGS.CALENDAR.APPROVE",
      "BOOKINGS.CALENDAR.VIEW",
      "BOOKINGS.SLOTS.VIEW",
    ]);
  });

  it("finds everything that would be left without its requirement, transitively", () => {
    expect([...dependentsOf("BOOKINGS.SLOTS.VIEW", index)].sort()).toEqual([
      "BOOKINGS.CALENDAR.APPROVE",
      "BOOKINGS.CALENDAR.REJECT",
      "BOOKINGS.CALENDAR.VIEW",
      "BOOKINGS.SLOTS.CREATE",
      "BOOKINGS.SLOTS.VIEW",
    ]);
  });
});

describe("ticking and unticking", () => {
  it("ticking an action ticks its VIEW and what that needs", () => {
    expect([...toggle(set(), "BOOKINGS.CALENDAR.APPROVE", true, index)].sort()).toEqual([
      "BOOKINGS.CALENDAR.APPROVE",
      "BOOKINGS.CALENDAR.VIEW",
      "BOOKINGS.SLOTS.VIEW",
    ]);
  });

  it("unticking a VIEW unticks every action that needs it, and leaves the rest", () => {
    const before = set("BOOKINGS.CALENDAR.VIEW", "BOOKINGS.CALENDAR.APPROVE", "BOOKINGS.SLOTS.VIEW", "ACCESS.ROLES.VIEW");
    expect([...toggle(before, "BOOKINGS.CALENDAR.VIEW", false, index)].sort()).toEqual(["ACCESS.ROLES.VIEW", "BOOKINGS.SLOTS.VIEW"]);
  });

  it("does not change the selection it was given", () => {
    const before = set("ACCESS.ROLES.VIEW");
    toggle(before, "ACCESS.ROLES.EDIT", true, index);
    expect([...before]).toEqual(["ACCESS.ROLES.VIEW"]);
  });
});

describe("what the caller may turn on", () => {
  it("needs the permission and everything it pulls in to be grantable or already on", () => {
    const grantable = set("BOOKINGS.CALENDAR.APPROVE", "BOOKINGS.CALENDAR.VIEW");
    expect(canTurnOn("BOOKINGS.CALENDAR.APPROVE", set(), grantable, index)).toBe(false); // SLOTS.VIEW is neither
    expect(canTurnOn("BOOKINGS.CALENDAR.APPROVE", set("BOOKINGS.SLOTS.VIEW"), grantable, index)).toBe(true);
  });

  it("refuses what is not grantable however its requirements stand", () => {
    expect(canTurnOn("ACCESS.ROLES.EDIT", set("ACCESS.ROLES.VIEW"), set("ACCESS.ROLES.VIEW"), index)).toBe(false);
  });
});

describe("the save", () => {
  it("lists what is added and what is removed", () => {
    expect(changes(["ACCESS.ROLES.VIEW", "BOOKINGS.SLOTS.VIEW"], set("BOOKINGS.SLOTS.VIEW", "BOOKINGS.SLOTS.CREATE"))).toEqual({
      added: ["BOOKINGS.SLOTS.CREATE"],
      removed: ["ACCESS.ROLES.VIEW"],
    });
  });

  it("gives the editor's columns in the catalogue's order, once each", () => {
    expect(actionsOf(catalogue)).toEqual(["VIEW", "APPROVE", "REJECT", "CREATE", "EDIT"]);
  });
});

describe("role codes from names", () => {
  it.each([
    ["Kitchen helper", "KITCHEN_HELPER"],
    ["  Dāna  committee 2 ", "DANA_COMMITTEE_2"],
    ["2nd shift", "ND_SHIFT"],
    ["මුළුතැන්ගෙය", ""],
    ["X", ""],
  ])("%j → %j", (name, code) => {
    expect(roleCodeFrom(name)).toBe(code);
  });
});
